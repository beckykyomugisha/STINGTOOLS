// TagConfigDeclarations - family name -> declared host category, from the
// shipped STING_TAG_CONFIG_v5_0_*.csv files.
//
// Revit-free on purpose, and split out of TagCategoryResolver so both dialects
// can be tested against the real files without opening Revit.
//
// WHY TWO DIALECTS
//
// The config is not one format. It grew two, and the parser only ever learned
// one:
//
//   1. The prose dialect - ARCH / GEN / MEP / STR and their
//      _DesignConstruction twins. A family header followed by attribute lines:
//
//          Tag Family #7: STING - Air Terminal Tag
//          TAG7: HVC_TAG_7_PARA_AT_TXT  -  Category: Air Terminals
//
//   2. The row dialect - HEALTH and HEALTH_DesignConstruction, written by the
//      Healthcare Pack. One comma-separated row per family, category in the
//      FOURTH field:
//
//          TAG_FAMILY,STING - Clinical Room Tag,H,Rooms,1,CLN_ROOM_CLASS_TXT,...
//
// Measured 2026-09-21: the prose dialect appears 311 times across 8 files and
// never in HEALTH; the row dialect appears 58 times in each HEALTH file and
// never elsewhere. Because ParseOne understood only the first, every healthcare
// family resolved to "no Category declared in STING_TAG_CONFIG_v5_0_*.csv" -
// and the Fix Categories audit reported 69 undeclared families, 58 of which
// were declared twice over, in this file and in
// TagFamilyCreatorCommand.HealthcareVariantFamilies.
//
// This is the failure this repo keeps producing: not a crash, not an error, a
// confident report of ABSENCE built by a reader that could not see the data.
// It nearly cost 58 hand-written category opinions replacing 58 correct
// declarations - including four medical-gas families that MgasNetwork.
// ClassifyRole recognises ONLY as Plumbing Fixtures and Pipe Accessories, and
// which the hand-written version would have scattered across three other
// categories and made invisible to the MGPS solver.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Tags
{
    /// <summary>One family's declared host category, and where it was found.</summary>
    public class TagDeclaration
    {
        /// <summary>Family name exactly as the config spells it.</summary>
        public string FamilyName { get; set; }
        /// <summary>Declared host category, e.g. "Air Terminals".</summary>
        public string HostCategory { get; set; }
        /// <summary>"prose" or "row" - which dialect declared it.</summary>
        public string Dialect { get; set; }
    }

    /// <summary>Parses the tag-config CSVs into family -> declared category.</summary>
    public static class TagConfigDeclarations
    {
        // Anchored at both ends: unanchored, "Tag Family" could match mid-line in
        // a description cell and capture rubbish as a family name.
        private static readonly Regex FamilyLine =
            new Regex(@"^Tag\s+Family\s*#\d+\s*:\s*(?<name>.+?)\s*$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CategoryLine =
            new Regex(@"Category\s*:\s*(?<cat>[^,•|]+)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parses one file's lines. Returns every declaration found, in file
        /// order, both dialects, without deciding what to do about duplicates -
        /// that is the caller's business, and it needs to log them.
        /// </summary>
        public static List<TagDeclaration> Parse(IEnumerable<string> lines)
        {
            var found = new List<TagDeclaration>();
            if (lines == null) return found;

            string current = null;

            foreach (string raw in lines)
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // --- row dialect ------------------------------------------------
                // Checked BEFORE the comment skip would matter, and before the
                // prose rules, because a TAG_FAMILY row is self-contained: it
                // carries its own name and category and must not inherit a
                // "current" family from a header above it.
                if (line.StartsWith("TAG_FAMILY,", StringComparison.OrdinalIgnoreCase))
                {
                    var fields = SplitCsv(line);
                    // 0 TAG_FAMILY | 1 name | 2 discipline | 3 category | ...
                    if (fields.Count > 3)
                    {
                        string name = fields[1].Trim().Trim('"');
                        string cat = fields[3].Trim().Trim('"');
                        if (name.Length > 0 && cat.Length > 0)
                            found.Add(new TagDeclaration
                            {
                                FamilyName = name,
                                HostCategory = cat,
                                Dialect = "row"
                            });
                    }
                    // A row never becomes the "current" family for prose lines.
                    continue;
                }

                if (line.StartsWith("#")) continue;

                // --- prose dialect ----------------------------------------------
                var fm = FamilyLine.Match(line);
                if (fm.Success)
                {
                    current = fm.Groups["name"].Value.Trim().Trim('"', ',');
                    continue;
                }

                if (current == null) continue;

                var cm = CategoryLine.Match(line);
                if (!cm.Success) continue;

                string pcat = cm.Groups["cat"].Value.Trim().Trim('"', ',');
                if (pcat.Length == 0) continue;

                found.Add(new TagDeclaration
                {
                    FamilyName = current,
                    HostCategory = pcat,
                    Dialect = "prose"
                });
            }

            return found;
        }

        /// <summary>
        /// Splits a CSV line, honouring double quotes so a category or
        /// description containing a comma does not shift the field positions.
        /// </summary>
        internal static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            if (line == null) return fields;

            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    // "" inside a quoted field is one literal quote.
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
