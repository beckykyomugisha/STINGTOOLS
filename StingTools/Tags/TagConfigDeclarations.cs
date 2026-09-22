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

        /// <summary>
        /// Which master this family takes its label from. Default
        /// <see cref="TagConfigDeclarations.UniversalGroup"/>.
        ///
        /// <para>Declared as "LabelMaster: LPS". A family is propagated to only
        /// by the master of its own group, so one library can carry several
        /// masters without any of them overwriting each other's families.</para>
        ///
        /// <para>This replaced a boolean. "Universal: No" meant SKIP, always,
        /// whichever master was running - which protected the nine LPS tags from
        /// the universal master and would equally have blocked the LPS master
        /// from reaching its own nine targets. A skip that cannot tell which
        /// master is asking is not a rule, it is a wall.</para>
        /// </summary>
        public string LabelMaster { get; set; } = TagConfigDeclarations.UniversalGroup;

        /// <summary>
        /// False when the family belongs to a group other than the universal
        /// one - it keeps its own bespoke label and must never receive the
        /// universal one.
        ///
        /// <para>This exists because the protection was previously an ACCIDENT.
        /// The nine LPS tags are Multi-Category, Revit refuses to move a clone
        /// into Multi-Category, and that error was the only thing stopping
        /// propagation overwriting them. Measured 2026-09-21: the universal
        /// master carries 71 generic ASS_* rows and ZERO ELC_LPS_* rows, so a
        /// successful propagation would have deleted the LPS class, zone,
        /// conductor material, cross-section, bond type, risk-assessment ref and
        /// both HIGH warnings - everything that makes them LPS tags.</para>
        ///
        /// <para>Build a Multi-Category master one day and that error stops
        /// firing, with nothing to replace it. Declaring the intent means the
        /// skip survives the circumstance that currently enforces it, and a
        /// tenth specialist tag - single-category or not - opts out the same
        /// way instead of rediscovering this.</para>
        /// </summary>
        public bool Universal
            => string.Equals(LabelMaster, TagConfigDeclarations.UniversalGroup,
                             StringComparison.OrdinalIgnoreCase);
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

        /// <summary>The group every family belongs to unless it says otherwise.</summary>
        public const string UniversalGroup = "universal";

        /// <summary>
        /// The group a legacy "Universal: No" resolves to.
        ///
        /// <para>It names no master, so nothing propagates to it - exactly the
        /// old behaviour. It is a distinct value rather than an empty string so
        /// the gates can tell "declared out of everything, the old way" from
        /// "declared into a named group", and report the first as something to
        /// migrate rather than as a silent exclusion.</para>
        /// </summary>
        public const string UnnamedGroup = "(unnamed)";

        // "LabelMaster: <group>" says which master serves this family.
        // "Universal: No" is the legacy spelling and still parses.
        //
        // Only an explicit NO counts. Anything else - absent, blank, misspelled,
        // "N0" - leaves the family opted IN, because the failure modes are not
        // symmetric: a family wrongly INCLUDED gets the universal label and is
        // recoverable from git, while a family wrongly EXCLUDED is silently
        // skipped forever and nobody notices. The gate in
        // TagConfigDeclarationsTests catches the typo that this leniency would
        // otherwise hide.
        private static readonly Regex UniversalLine =
            new Regex(@"Universal\s*:\s*(?<val>[A-Za-z]+)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LabelMasterLine =
            new Regex(@"LabelMaster\s*:\s*(?<val>[A-Za-z0-9_-]+)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The label-master group declared on this line.
        ///
        /// <para>"LabelMaster: X" wins. A legacy "Universal: No" gives
        /// <see cref="UnnamedGroup"/>. Anything else - absent, blank, a typo -
        /// gives <see cref="UniversalGroup"/>, because the two failure modes are
        /// not symmetric: a family wrongly INCLUDED gets the universal label and
        /// is recoverable from git, while a family wrongly EXCLUDED is silently
        /// passed over on every run forever. The gates catch the typo that this
        /// leniency would otherwise hide.</para>
        /// </summary>
        internal static string DeclaredLabelMaster(string line)
        {
            if (string.IsNullOrEmpty(line)) return UniversalGroup;

            var m = LabelMasterLine.Match(line);
            if (m.Success)
            {
                string g = m.Groups["val"].Value;
                // "LabelMaster: universal" is just the default said out loud.
                return string.Equals(g, UniversalGroup, StringComparison.OrdinalIgnoreCase)
                    ? UniversalGroup : g;
            }

            return DeclaresNonUniversal(line) ? UnnamedGroup : UniversalGroup;
        }

        /// <summary>True when the text declares an explicit "Universal: No".</summary>
        internal static bool DeclaresNonUniversal(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var m = UniversalLine.Match(line);
            if (!m.Success) return false;
            string v = m.Groups["val"].Value;
            return string.Equals(v, "No", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "False", StringComparison.OrdinalIgnoreCase);
        }

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
                                Dialect = "row",
                                // A row has no free-text line to carry it, so scan
                                // the whole row. Both dialects must be able to say
                                // this: a family declared in only one of them would
                                // otherwise opt out in prose and back in by row,
                                // and the merge would pick whichever came first.
                                LabelMaster = DeclaredLabelMaster(line)
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
                    Dialect = "prose",
                    // Read off the SAME line as the category, which is where the
                    // TAG7 line already carries the family's own declarations.
                    LabelMaster = DeclaredLabelMaster(line)
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
