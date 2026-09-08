using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The shipped PROD data, asserted. The RESOLVER had fifteen tests; the two CSVs it
    /// reads had none — the same asymmetry the material schedule carried until a drift
    /// gate was put over its constants.
    ///
    /// <para>Three defects were sitting in the data, and every one of them resolved to a
    /// confident wrong code rather than to an error:</para>
    ///
    /// <list type="number">
    /// <item><b>Ten rules were shadowed.</b> <c>*Boiler Feed*</c> sat below
    /// <c>*Boiler*</c>, <c>*Fire Damper*</c> below <c>*Damper*</c>, <c>*Fume Hood*</c>
    /// below <c>*Hood*</c>, <c>*Mop Sink*</c> below <c>*Sink*</c>. Three could never fire
    /// at all. Ten different authors made the same mistake, which is what says the file —
    /// which reads like a dictionary of independent rules — was behaving like an ordered
    /// chain. Fixed in the matcher (most specific wins), not by reordering rows.</item>
    ///
    /// <item><b>Unanchored material stems matched inside longer words.</b>
    /// "Sh<b>eeps</b> Wool Insulation" took the EPS code: a real insulation handed a
    /// different insulation's code. Fixed in the data, because only the author knows that
    /// <c>galv</c> is a deliberate prefix and <c>eps</c> is not.</item>
    ///
    /// <item><b>One PROD code declared two disciplines.</b> VIE / ZVB / AAP each said P
    /// in one row and MG in another, so a tag's discipline would depend on which category
    /// somebody modelled the thing in.</item>
    /// </list>
    ///
    /// <para>Only columns 0-2 of STING_PROD_CODES.csv are read by the plugin today, so
    /// finding 3 is currently inert. It is asserted anyway: an inert disagreement is a
    /// live one the day someone wires the DISCIPLINE column, and it costs nothing to
    /// keep the file honest until then.</para>
    /// </summary>
    public class ProdCodeDataTests
    {
        // ── the shipped files, read from source rather than a copy ──────────────
        // Deliberately NOT copied to the output directory: a stale copy passing while
        // the shipped file is wrong is the exact failure this class exists to stop.

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private sealed class Row
        {
            public int Line;
            public string Code = "", Category = "", Pattern = "", Discipline = "";
            public override string ToString() => $"line {Line} [{Category}] '{Pattern}' -> {Code}";
        }

        private static List<Row> ProdRows()
        {
            var rows = new List<Row>();
            var lines = File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv"));
            for (int i = 1; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var c = line.Split(',');
                if (c.Length < 3) continue;
                string code = c[0].Trim(), cat = c[1].Trim(), pat = c[2].Trim().ToUpperInvariant();
                if (code.Length == 0 || cat.Length == 0 || pat.Length == 0) continue;
                rows.Add(new Row
                {
                    Line = i + 1,
                    Code = code,
                    Category = cat,
                    Pattern = pat,
                    Discipline = c.Length > 4 ? c[4].Trim() : "",
                });
            }
            Assert.True(rows.Count > 100, "STING_PROD_CODES.csv looks empty: " + rows.Count + " rows");
            return rows;
        }

        /// <summary>The alternatives of a glob cell, and the shortest name each was
        /// written to catch (its literal, wildcards stripped).</summary>
        private static IEnumerable<string> Literals(string pattern)
            => pattern.Split('|')
                      .Select(a => a.Trim().Replace("*", "").Replace("?", "").Trim())
                      .Where(a => a.Length > 0);

        private static List<(string Pattern, string ProdCode)> ForCategory(List<Row> rows, string cat)
            => rows.Where(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase))
                   .Select(r => (r.Pattern, r.Code)).ToList();

        // ══════════════════════════════════════════════════════════════════════
        //  1. No rule is unreachable
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Every shipped rule wins on a name written to satisfy it.
        ///
        /// <para>This is the whole of finding 1, made executable: feed each alternative
        /// the literal it was authored for, resolve it through the REAL resolver against
        /// the REAL category rule list, and require the authoring row to be the one that
        /// answers. Before the specificity change, ten rows failed here.</para>
        /// </summary>
        [Fact]
        public void Every_Shipped_Rule_Wins_On_Its_Own_Name()
        {
            var rows = ProdRows();
            var byCat = rows.GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.Select(r => (r.Pattern, r.Code)).ToList(),
                                          StringComparer.OrdinalIgnoreCase);

            var dead = new List<string>();
            foreach (var row in rows)
                foreach (string lit in Literals(row.Pattern))
                {
                    // The one exclusion, named rather than tolerated. BHD and BHT are two
                    // codes for ONE product and both spell "*Bedhead*", so they tie on
                    // specificity and the tie correctly falls to file order — no ranking
                    // can separate them because there is nothing to separate. Retiring one
                    // is a healthcare-catalogue decision (E vs H discipline), not a
                    // mechanical one. KnownUnresolved_Bedhead_Trunking_Has_Two_Codes fails
                    // the day it is settled, so this cannot outlive the problem.
                    if (row.Code == "BHT" && lit == "BEDHEAD") continue;

                    string got = ProdResolver.Resolve(
                        familyName: "F", typeName: lit, categoryName: row.Category,
                        projRulesForCategory: null, corpRulesForCategory: byCat[row.Category],
                        prodMap: null, source: out _);

                    if (!string.Equals(got, row.Code, StringComparison.Ordinal))
                        dead.Add($"{row}  — the name '{lit}' it was written for resolves to {got}");
                }

            Assert.True(dead.Count == 0,
                "Shipped PROD rules that a more general rule in the same category swallows. " +
                "A name the author wrote a rule for resolves to somebody else's code, silently:\n  "
                + string.Join("\n  ", dead));
        }

        /// <summary>The guard above is only worth having if it fires. This is the exact
        /// pair that was live in the file — a general rule, then a specific one — with
        /// the specificity ranking removed by hand.</summary>
        [Fact]
        public void The_Unreachable_Guard_Actually_Fires_Under_First_Match_Wins()
        {
            var rules = new List<(string, string)>
            {
                ("*DAMPER*", "DMP"),        // general, first
                ("*FIRE DAMPER*", "FSD"),   // specific, second — the shipped order
            };

            // What the resolver does NOW: the specific rule wins wherever it applies.
            Assert.Equal("FSD", ProdResolver.Resolve("F", "FIRE DAMPER", "Ducts",
                                                     null, rules, null, out _));
            Assert.Equal("DMP", ProdResolver.Resolve("F", "VOLUME DAMPER", "Ducts",
                                                     null, rules, null, out _));

            // What plain first-match-wins did: the general rule ate it. Kept as a literal
            // so this test states the defect rather than describing it in prose.
            string firstMatch = rules.First(r => ProdPatternMatcher.Matches("FIRE DAMPER", r.Item1)).Item2;
            Assert.Equal("DMP", firstMatch);
        }

        /// <summary>Ties keep file order, so nothing that used to resolve one way starts
        /// resolving the other for want of a tie-break.</summary>
        [Fact]
        public void Equally_Specific_Rules_Keep_File_Order()
        {
            var rules = new List<(string, string)> { ("*PUMP*", "AAA"), ("*PUMP*", "BBB") };
            Assert.Equal("AAA", ProdResolver.Resolve("F", "PUMP 01", "Mechanical Equipment",
                                                     null, rules, null, out _));
        }

        /// <summary>A project overlay still beats corporate even when the corporate rule
        /// is the more specific of the two — the tiers are a chain, only the rules WITHIN
        /// a tier are peers.</summary>
        [Fact]
        public void A_Vaguer_Project_Rule_Still_Beats_A_Sharper_Corporate_One()
        {
            string got = ProdResolver.Resolve(
                "F", "FIRE DAMPER", "Ducts",
                projRulesForCategory: new List<(string, string)> { ("*DAMPER*", "PRJ") },
                corpRulesForCategory: new List<(string, string)> { ("*FIRE DAMPER*", "FSD") },
                prodMap: null, source: out string source);

            Assert.Equal("PRJ", got);
            Assert.Equal(ProdResolver.Sources.Project, source);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1b. A system-category rule must need the TYPE name
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Families that are not loadable — every element in these categories
        /// shares the family name, so it discriminates nothing.</summary>
        private static readonly (string Category, string Family)[] SystemFamilies =
        {
            ("Walls", "Basic Wall"), ("Walls", "Curtain Wall"), ("Walls", "Stacked Wall"),
            ("Roofs", "Basic Roof"), ("Roofs", "Sloped Glazing"),
            ("Floors", "Floor"),
            ("Ceilings", "Compound Ceiling"), ("Ceilings", "Basic Ceiling"),
            ("Ducts", "Rectangular Duct"), ("Ducts", "Round Duct"), ("Ducts", "Oval Duct"),
            ("Pipes", "Pipe Types"),
            ("Structural Foundations", "Wall Foundation"), ("Structural Foundations", "Foundation Slab"),
        };

        /// <summary>
        /// All 11 shipped rules on a system category resolve on the TYPE name, and none
        /// on the family name alone.
        ///
        /// <para>This is what makes the audit's advice true. `Prod_GenerateRules`
        /// deliberately SKIPS system families — a project overlay row keyed on
        /// "Basic Wall" would match every wall in the model, a category default wearing
        /// a family rule's clothes — so the only route to a specific PROD code for a
        /// wall is a rule keyed on its type. If someone adds a bare family rule here it
        /// will not error; it will quietly give every wall in every project one code.</para>
        ///
        /// <para>The resolver matches against <c>"{family} {type}"</c>, so a wall with no
        /// type name resolved against <c>"Basic Wall "</c> — which is what this feeds.</para>
        /// </summary>
        [Fact]
        public void No_Shipped_Rule_On_A_System_Category_Matches_The_Bare_Family_Name()
        {
            var rows = ProdRows();
            var offenders = new List<string>();

            foreach (var (cat, fam) in SystemFamilies)
                foreach (var row in rows.Where(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase)))
                    if (ProdPatternMatcher.Matches((fam + " ").ToUpperInvariant(), row.Pattern))
                        offenders.Add($"{row} matches the bare family name '{fam}'");

            Assert.True(offenders.Count == 0,
                "A PROD rule on a system category that matches the FAMILY name alone gives EVERY element "
                + "in that category the same code — the family name is shared by all of them. Put the "
                + "material, size or service test in the pattern so it needs the TYPE name:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_System_Family_Guard_Actually_Fires()
        {
            // The row it exists to catch, fed to the same matcher.
            Assert.True(ProdPatternMatcher.Matches("BASIC WALL ", "*BASIC WALL*"));
        }

        [Fact]
        public void And_Those_Rules_Still_Resolve_Once_A_Type_Name_Arrives()
        {
            // The guard above must not be satisfiable by shipping rules that match
            // NOTHING. This is the payoff half: a floor typed "Concrete Slab 200"
            // resolves to SLB, which is only reachable because the resolver reads
            // GetElementTypeName rather than GetFamilySymbolName (KUT-11).
            var rows = ProdRows();
            Assert.Equal("SLB", ProdResolver.Resolve(
                "Floor", "Concrete Slab 200", "Floors",
                null, ForCategory(rows, "Floors"), null, out string src));
            Assert.Equal(ProdResolver.Sources.Corporate, src);

            // ...and the same floor with NO type name cannot resolve, which is exactly
            // the state the audit used to REPORT (blank Type column) while the resolver
            // was seeing the type all along.
            ProdResolver.Resolve("Floor", "", "Floors",
                null, ForCategory(rows, "Floors"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Floors"] = "FLR" },
                out string blindSrc);
            Assert.False(ProdResolver.IsSpecific(blindSrc));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. One code, one discipline
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A PROD code may appear in several categories — a zone valve box is modelled as
        /// Pipe Accessories on one job and Specialty Equipment on the next, and both rows
        /// are wanted. What it may NOT do is declare a different discipline in each, or
        /// the tag's discipline segment depends on how somebody chose to model it.
        /// </summary>
        [Fact]
        public void No_Prod_Code_Declares_Two_Disciplines()
        {
            var split = ProdRows()
                .Where(r => r.Discipline.Length > 0)
                .GroupBy(r => r.Code, StringComparer.Ordinal)
                .Where(g => g.Select(r => r.Discipline).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => $"{g.Key} -> " + string.Join(" / ", g.Select(r => $"{r.Discipline} (line {r.Line}, {r.Category})")))
                .ToList();

            Assert.True(split.Count == 0,
                "One PROD code, two disciplines. The DISCIPLINE column is not read by the " +
                "plugin today, so this is documentation — but it is documentation the next " +
                "person will wire up:\n  " + string.Join("\n  ", split));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. Material stems match words, not runs of letters
        // ══════════════════════════════════════════════════════════════════════

        private static List<MaterialProdRule> MaterialRules()
        {
            var warnings = new List<string>();
            var rules = MaterialProdOverrideRules.Parse(
                File.ReadAllLines(Path.Combine(DataDir(), "STING_MATERIAL_PROD_OVERRIDES.csv")), warnings);
            Assert.True(warnings.Count == 0, "Unparseable rows: " + string.Join("; ", warnings));
            Assert.True(rules.Count > 20, "Override table looks empty: " + rules.Count);
            return rules;
        }

        /// <summary>
        /// The four misfires found by running the shipped table over real material names.
        /// Sheep's wool is the one worth remembering: a genuine insulation being handed a
        /// DIFFERENT insulation's code reads as a fact, not as a fault.
        /// </summary>
        [Theory]
        [InlineData("Sheeps Wool Insulation")]     // matched (?i)eps
        [InlineData("Acoustic Spiral Liner")]      // matched (?i)pir
        [InlineData("Respirator Filter Housing")]  // matched (?i)pir
        [InlineData("Prefabricated Ductwork")]     // matched (?i)fabric
        [InlineData("Aspirating Detector Housing")]
        [InlineData("Empire Panelling")]
        public void A_Stem_Inside_A_Longer_Word_Is_Not_A_Material(string materialName)
        {
            string suffix = MaterialProdOverrideRules.ResolveSuffix(MaterialRules(), materialName, "Generic Models");
            Assert.True(suffix == null,
                $"'{materialName}' took the material suffix -{suffix} from a stem buried inside " +
                "one of its words. Anchor that alternative in STING_MATERIAL_PROD_OVERRIDES.csv.");
        }

        /// <summary>
        /// The other half, and the reason this is curated data rather than an inferred
        /// rule: several stems are DELIBERATE prefixes. Anchoring both ends of every
        /// alternative would have traded one silent wrong answer for another.
        /// </summary>
        [Theory]
        // Neither of these names contains ANY other stem from its row, so only the
        // open-ended prefix can answer them. "Galvanised Steel Sheet" would not do:
        // it also contains "Steel", so it passes whether or not \bgalv still works,
        // and a mutation over-anchoring galv sailed straight through it.
        [InlineData("Galvanised Finish", "STL")]        // \bgalv is meant to be open-ended
        [InlineData("Cementitious Screed", "CON")]      // so is \bcement
        [InlineData("Steelwork", "STL")]
        [InlineData("Plasterboard 12.5mm", "GYP")]
        [InlineData("Blockwork 200", "MAS")]
        [InlineData("Stonework Cladding", "STN")]
        [InlineData("uPVC Window Frame", "PLA")]        // leading edge deliberately open
        [InlineData("cPVC Pipe", "PLA")]
        [InlineData("EPS Insulation Board", "INS-EPS")]
        [InlineData("PIR Board 100mm", "INS-PIR")]
        [InlineData("Rigid Polyurethane", "INS-PIR")]
        [InlineData("Fabrics - Upholstery", "FAB")]
        [InlineData("Porcelain 600x600", "TIL")]
        [InlineData("Concrete C25/30", "CON")]
        [InlineData("Mineral Wool Quilt", "INS-MW")]
        [InlineData("EPDM Membrane", "RBR")]
        [InlineData("Lead Sheet Code 5", "PB")]
        public void A_Real_Material_Still_Resolves(string materialName, string expected)
        {
            Assert.Equal(expected,
                MaterialProdOverrideRules.ResolveSuffix(MaterialRules(), materialName, "Structural Framing"));
        }

        /// <summary>
        /// Known-unresolved, named rather than quietly tolerated.
        ///
        /// <para>"Lead-Free Solder" still takes -PB. That is not a boundary problem — the
        /// word IS "lead", correctly bounded — it is a NEGATION the table has no way to
        /// express, and inventing a <c>(?!-free)</c> special case invites tin-free,
        /// chrome-free and every other one after it. It is recorded here so the next
        /// reader meets it as a known limit rather than as a fresh surprise; the test
        /// fails if it is ever fixed, so the note cannot rot.</para>
        /// </summary>
        [Fact]
        public void KnownUnresolved_A_Negated_Material_Name_Still_Matches_The_Material()
        {
            Assert.Equal("PB",
                MaterialProdOverrideRules.ResolveSuffix(MaterialRules(), "Lead-Free Solder", "Pipe Fittings"));
        }

        /// <summary>
        /// Known-unresolved: BHD and BHT are two PROD codes for one product (bedhead
        /// trunking), under two disciplines (E and H). Which survives is a standards call
        /// for whoever owns the healthcare catalogue, not a mechanical one — so it is
        /// named here instead of being decided quietly in a commit.
        /// </summary>
        [Fact]
        public void KnownUnresolved_Bedhead_Trunking_Has_Two_Codes()
        {
            var codes = ProdRows()
                .Where(r => r.Pattern.Contains("BEDHEAD"))
                .Select(r => r.Code).Distinct().OrderBy(c => c).ToList();

            Assert.Equal(new[] { "BHD", "BHT" }, codes);
        }
    }
}
