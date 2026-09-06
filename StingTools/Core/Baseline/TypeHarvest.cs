// ══════════════════════════════════════════════════════════════════════════
//  TypeHarvest.cs — build a catalogue pack from what a delivered model
//  actually contains.
//
//  This is what stops the catalogue ossifying. The shipped starter pack is
//  one person's reading of the market; a harvested pack is a record of work
//  that was built and paid for. After two or three delivered projects the
//  starter should be replaced outright rather than edited.
//
//  PLACED types only. A type sitting in the browser that nobody ever used is
//  not evidence of practice — it is evidence that somebody loaded a family.
//  Counting it would let an unused vendor library masquerade as a standard.
//
//  Harvested names are PREFIXED, not renamed in place. The pack proposes
//  "STING Flush Door 900x2100" and records the original name and instance
//  count in its purpose, so a reviewer can see exactly what was observed
//  before promoting anything to a corporate pack.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    /// <summary>One placed type, as the Revit side observed it.</summary>
    public sealed class HarvestedType
    {
        public string Category = "";
        public string FamilyName = "";
        public string TypeName = "";
        /// <summary>How many instances are placed. Zero means it was not placed
        /// and must never reach a pack.</summary>
        public int InstanceCount;
        /// <summary>Parameter name → value in mm, for the parameters that exist.</summary>
        public Dictionary<string, double> ParametersMm =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TypeHarvestReport
    {
        public int TypesObserved;
        public int TypesHarvested;
        public int SkippedNotPlaced;
        public int SkippedAlreadyPrefixed;

        /// <summary>
        /// Harvested, but with no dimension at all — the real pack contained
        /// `STING 1000` from a Window-Square Opening with "Parameters": [].
        /// Minting it produces a renamed clone that defines nothing, so it is
        /// kept (it IS placed practice) and named, rather than dropped.
        /// </summary>
        public int TypesWithNoDimensions;

        /// <summary>
        /// Types whose NAME contains numbers that appear in none of their
        /// harvested parameters. The real pack held three columns all named
        /// "200x200 with 12.5 plaster" measuring 175, 450 and 500 square, and a
        /// vendor door named 1740x2595mm measuring 1500 x 2400.
        ///
        /// A model problem, not a plugin one — but the harvest is where it
        /// becomes visible, and a catalogue whose names contradict its own
        /// numbers is worse than no catalogue.
        /// </summary>
        public readonly List<string> NameDisagreesWithSize = new List<string>();
        public readonly Dictionary<string, int> ByCategory =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Summary()
        {
            if (TypesObserved == 0)
                return "Nothing to harvest: no placed instances were found in the "
                     + "categories a catalogue pack can describe.";

            string s = $"Harvested {TypesHarvested} placed type(s) from {TypesObserved} observed";
            if (ByCategory.Count > 0)
                s += " (" + string.Join(", ", ByCategory.OrderBy(k => k.Key)
                        .Select(k => $"{k.Key} {k.Value}")) + ")";
            s += ".";

            if (SkippedNotPlaced > 0)
                s += $" {SkippedNotPlaced} type(s) skipped as never placed — a type nobody used "
                   + "is not evidence of practice.";
            if (SkippedAlreadyPrefixed > 0)
                s += $" {SkippedAlreadyPrefixed} skipped as already STING-minted: harvesting our "
                   + "own types back would make the catalogue a record of itself.";

            if (TypesWithNoDimensions > 0)
                s += $" {TypesWithNoDimensions} type(s) carry NO dimension the rule for their "
                   + "category recognises, so minting them would rename a type without defining "
                   + "it. They are kept because they are placed practice — decide per type "
                   + "whether the pack should keep them.";

            if (NameDisagreesWithSize.Count > 0)
                s += $" {NameDisagreesWithSize.Count} type NAME(s) contain sizes that appear in "
                   + "none of their own parameters: "
                   + string.Join(", ", NameDisagreesWithSize.Take(4).Select(n => "'" + n + "'"))
                   + (NameDisagreesWithSize.Count > 4 ? ", …" : "")
                   + ". That is the MODEL disagreeing with itself, not a harvest fault — but a "
                   + "catalogue whose names contradict its numbers is worse than none, so fix the "
                   + "names or the parameters before adopting.";
            return s;
        }
    }

    public static class TypeHarvestBuilder
    {
        /// <summary>
        /// Turn observations into a pack. Never writes; the caller decides
        /// whether the result is worth keeping.
        /// </summary>
        public static TypeCatalogueLibrary Build(IEnumerable<HarvestedType> observed,
                                                 string projectCode,
                                                 TypeHarvestReport report)
        {
            report = report ?? new TypeHarvestReport();
            var pack = new TypeCataloguePack
            {
                Id = "HARVEST-" + Sanitise(projectCode) + "-V1",
                Title = "Harvested from " + (string.IsNullOrWhiteSpace(projectCode)
                                             ? "an unnamed project" : projectCode.Trim()),
                Status = "provisional",
                SourceNote = "Harvested from the types actually PLACED in "
                    + (string.IsNullOrWhiteSpace(projectCode) ? "a delivered model" : projectCode.Trim())
                    + " on " + DateTime.Now.ToString("yyyy-MM-dd")
                    + ". These are observations of one project, not a standard: review the sizes, "
                    + "rename the pack, and adopt it deliberately before relying on it. Type names "
                    + "carry the STING prefix so they can be minted; the original name and instance "
                    + "count are recorded against each one."
            };

            foreach (var h in observed ?? Enumerable.Empty<HarvestedType>())
            {
                if (h == null || string.IsNullOrWhiteSpace(h.TypeName)
                    || string.IsNullOrWhiteSpace(h.Category)) continue;
                report.TypesObserved++;

                if (h.InstanceCount <= 0) { report.SkippedNotPlaced++; continue; }

                // Harvesting our own minted types back would make the catalogue
                // a record of itself rather than of the project.
                if (h.TypeName.StartsWith(BaselineFamilyType.MintedPrefix, StringComparison.Ordinal))
                { report.SkippedAlreadyPrefixed++; continue; }

                var ft = new BaselineFamilyType
                {
                    Category = h.Category.Trim(),
                    TypeName = BaselineFamilyType.MintedPrefix + h.TypeName.Trim(),
                    FamilyNamePatterns = PatternsFor(h.FamilyName),
                    Purpose = $"observed as [{h.TypeName.Trim()}] in family [{h.FamilyName}], "
                            + $"{h.InstanceCount} instance(s) placed"
                };
                // Only the parameters that DEFINE a type in this category. The
                // reader is expected to have filtered already; this is the
                // second gate, because a pack built by anything else must not
                // be able to smuggle a column's extents in as its section.
                var allowed = HarvestDimensionRules.For(h.Category);
                foreach (var kv in h.ParametersMm ?? new Dictionary<string, double>())
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0
                        && allowed.Any(a => string.Equals(a, kv.Key, StringComparison.OrdinalIgnoreCase)))
                        ft.Parameters.Add(new BaselineFamilyTypeParam { Name = kv.Key, ValueMm = kv.Value });

                if (ft.Parameters.Count == 0) report.TypesWithNoDimensions++;
                else if (NameDisagrees(h.TypeName, ft.Parameters))
                    report.NameDisagreesWithSize.Add(h.TypeName.Trim());

                pack.FamilyTypes.Add(ft);
                report.TypesHarvested++;
                report.ByCategory.TryGetValue(ft.Category, out int n);
                report.ByCategory[ft.Category] = n + 1;
            }

            return new TypeCatalogueLibrary
            {
                Note = "Harvested catalogue. Review before adopting: nothing here applies to any "
                     + "project until its id is listed in adoptCatalogues.",
                Packs = { pack }
            };
        }

        /// <summary>
        /// True when the type NAME contains a number that no harvested
        /// parameter carries.
        ///
        /// Deliberately conservative — it does not try to decide WHICH
        /// parameter a number in the name refers to, because it cannot know.
        /// It only reports that a name says 200 while nothing in the type
        /// measures 200, which is a fact worth surfacing and not an inference.
        ///
        /// Numbers under 10 are ignored: "Type 2" and "12.5 plaster" are
        /// naming, not dimensions, and flagging them would bury the real cases.
        /// </summary>
        private static bool NameDisagrees(string typeName, List<BaselineFamilyTypeParam> parameters)
        {
            if (string.IsNullOrWhiteSpace(typeName) || parameters == null || parameters.Count == 0)
                return false;

            var inName = System.Text.RegularExpressions.Regex
                .Matches(typeName, @"\d+(?:\.\d+)?")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => double.TryParse(m.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : -1)
                .Where(d => d >= 10)
                .ToList();
            if (inName.Count == 0) return false;

            // Every number in the name must be findable, to the millimetre, in
            // some parameter. One that is not means the name is describing a
            // size the type does not have.
            return inName.Any(n => !parameters.Any(p => Math.Abs(p.ValueMm - n) < 0.5));
        }

        /// <summary>
        /// The family's own name is the only pattern that can be trusted to find
        /// it again. A cleverer guess ("Door" from "M_Single-Flush") would match
        /// families the observation never saw.
        /// </summary>
        private static List<string> PatternsFor(string familyName) =>
            string.IsNullOrWhiteSpace(familyName)
                ? new List<string>()
                : new List<string> { familyName.Trim() };

        private static string Sanitise(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "PROJECT";
            var kept = code.Trim().ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
            return kept.Length == 0 ? "PROJECT" : new string(kept);
        }
    }
}
