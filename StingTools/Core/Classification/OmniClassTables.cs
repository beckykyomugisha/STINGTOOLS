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

namespace StingTools.Core.Classification
{
    public sealed class OmniClassTableInfo
    {
        public string Number;     // "21"
        public string Name;       // "Elements"
        public bool IsSpatial;    // true ⇒ classify the host room, not the element
        public string MapFile;    // "STING_OMNICLASS_21_MAP.csv"

        /// <summary>e.g. "Table 21 — Elements".</summary>
        public string Label => $"Table {Number} — {Name}";
    }

    public static class OmniClassTables
    {
        private static readonly Dictionary<string, OmniClassTableInfo> Known =
            new Dictionary<string, OmniClassTableInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["21"] = new OmniClassTableInfo { Number = "21", Name = "Elements",            IsSpatial = false, MapFile = "STING_OMNICLASS_21_MAP.csv" },
            ["13"] = new OmniClassTableInfo { Number = "13", Name = "Spaces by Function",  IsSpatial = true,  MapFile = "STING_OMNICLASS_13_MAP.csv" },
            ["23"] = new OmniClassTableInfo { Number = "23", Name = "Products",            IsSpatial = false, MapFile = "STING_OMNICLASS_23_MAP.csv" },
            ["22"] = new OmniClassTableInfo { Number = "22", Name = "Work Results",        IsSpatial = false, MapFile = "STING_OMNICLASS_22_MAP.csv" },
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
                IsSpatial = false,
                MapFile = $"STING_OMNICLASS_{n}_MAP.csv"
            };
        }

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
