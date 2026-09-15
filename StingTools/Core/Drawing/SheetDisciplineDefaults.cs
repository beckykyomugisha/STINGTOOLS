// StingTools — Drawing Template Manager · the built-in discipline vocabulary
//
// These are the FALLBACK, not the source of truth: SheetDisciplineConfig layers
// Data/STING_SHEET_DISCIPLINES.json and a per-project file on top, and the
// resolver reads through the config rather than from here.
//
// They stay in code on purpose. A shipped data file can be absent — a broken
// install, a deploy that dropped data/, a project folder someone tidied — and
// sheet numbering that stops working because a JSON file went missing is a worse
// failure than one that quietly uses sensible defaults and says so.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    internal static class SheetDisciplineDefaults
    {
        private static TitleKeywordRule Rule(string disc, params string[] words)
            => new TitleKeywordRule { Discipline = disc, Words = new List<string>(words) };

        /// <summary>Sheet-number prefixes, as drawing sets actually number them.</summary>
        internal static readonly Dictionary<string, string> NumberPrefixes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "A", "A" }, { "AR", "A" }, { "ARCH", "A" },
                { "S", "S" }, { "ST", "S" }, { "STR", "S" },
                { "M", "M" }, { "MEC", "M" }, { "MECH", "M" }, { "H", "M" }, { "HVAC", "M" },
                { "E", "E" }, { "EL", "E" }, { "ELE", "E" }, { "ELEC", "E" },
                { "P", "P" }, { "PL", "P" }, { "PLM", "P" }, { "PH", "P" },
                { "C", "C" }, { "CIV", "C" },
                { "L", "L" }, { "LA", "L" },
                { "FP", "FP" }, { "FA", "FP" },
                { "LV", "LV" }, { "ICT", "LV" }, { "IT", "LV" },
                { "I", "I" }, { "ID", "I" },
                { "CO", "COORD" }, { "CD", "COORD" }, { "COORD", "COORD" },
                { "G", "GEN" }, { "GEN", "GEN" },
            };

        /// <summary>Whole words in a sheet title, in decision order.</summary>
        internal static readonly List<TitleKeywordRule> TitleKeywords =
            new List<TitleKeywordRule>
            {
                // Explicitly multidisciplinary beats every single-discipline word,
                // because "COORDINATED SERVICES PLAN" names both.
                Rule("COORD",
                    "COORDINATION", "COORDINATED", "COMBINED", "COMPOSITE", "MULTIDISCIPLINARY"),
                Rule("FP",
                    "SPRINKLER", "SPRINKLERS", "FIREFIGHTING", "HYDRANT", "SUPPRESSION"),
                Rule("LV",
                    "SECURITY", "CCTV", "TELECOMS", "TELECOM", "STRUCTURED", "AUDIOVISUAL"),
                Rule("M",
                    "MECHANICAL", "HVAC", "DUCTWORK", "VENTILATION", "REFRIGERATION"),
                Rule("E",
                    "ELECTRICAL", "LIGHTING", "POWER", "SMALL"),
                Rule("P",
                    "PLUMBING", "SANITARY", "DRAINAGE", "ABOVE", "BELOW"),
                Rule("S",
                    "STRUCTURAL", "FOUNDATION", "FOUNDATIONS", "REBAR", "REINFORCEMENT", "FRAMING"),
                Rule("C",
                    "CIVIL", "EARTHWORKS", "ROADS", "HIGHWAY", "HIGHWAYS"),
                Rule("L",
                    "LANDSCAPE", "LANDSCAPING", "PLANTING", "SOFTWORKS"),
                Rule("I",
                    "INTERIOR", "INTERIORS", "JOINERY", "FF&E", "FFE"),
                Rule("A",
                    "ARCHITECTURAL", "ARCHITECTURE", "ELEVATION", "ELEVATIONS",
                            "SECTION", "SECTIONS", "FLOOR", "ROOF", "DOOR", "DOORS",
                            "WINDOW", "WINDOWS", "FINISHES", "GA"),
            };
        /// <summary>Discipline code to TITLE_BLOCK.csv column name.</summary>
        internal static readonly Dictionary<string, string> CsvColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", "ARCH" }, { "ARCH", "ARCH" },
                { "S", "STR" }, { "STR", "STR" },
                { "M", "MEP" }, { "MEP", "MEP" },
                { "E", "ELE" }, { "ELE", "ELE" },
                { "P", "PLM" }, { "PLM", "PLM" },
                { "FP", "FP" }, { "LV", "LV" },
                { "COORD", "COORD" }, { "GEN", "GEN" },
            };
    }
}
