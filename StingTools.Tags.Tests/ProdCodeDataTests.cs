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
            // PROD-3: the reference-only columns. The plugin reads 0-2 and nothing else,
            // so nothing at runtime can notice a wrong value here. These tests can.
            public string Description = "", System = "", StandardRef = "";
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
                    Discipline  = c.Length > 4 ? c[4].Trim() : "",
                    Description = c.Length > 3 ? c[3].Trim() : "",
                    System      = c.Length > 5 ? c[5].Trim() : "",
                    StandardRef = c.Length > 6 ? c[6].Trim() : "",
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

        /// <summary>
        /// The CATCH-ALL system family of each category: the name Revit gives an element
        /// that has not been specialised into some other kind. These discriminate
        /// nothing, because everything lands there by default.
        ///
        /// <para><b>Not every system family qualifies, and that distinction is the
        /// point.</b> "Curtain Wall" and "Sloped Glazing" are also system families, but
        /// they name a distinct KIND: a rule matching them gives every curtain wall one
        /// code, which is a true statement about curtain walls. A rule matching "Basic
        /// Wall" gives every wall in the model one code, which is the category default
        /// wearing a rule's clothes.</para>
        ///
        /// <para>This list NARROWED when the architectural fabric rules landed and this
        /// gate rejected <c>*Curtain Wall*</c> and <c>*Sloped Glazing*</c>. Narrowing a
        /// gate to admit one's own new rules is exactly how a gate stops meaning
        /// anything, so the justification is worth stating: the original reason — "every
        /// element shares this family name, so it discriminates nothing" — was simply
        /// UNTRUE of those two. Ducts, Pipes and Structural Foundations left for the
        /// same reason; "Rectangular Duct" and "Wall Foundation" are kinds, and neither
        /// category has a catch-all at all.
        /// <c>The_System_Family_Guard_Actually_Fires</c> proves Basic Wall is still
        /// caught, and <c>A_Kind_Bearing_System_Family_May_Be_Matched</c> pins the other
        /// half so this cannot quietly widen back.</para>
        /// </summary>
        private static readonly (string Category, string Family)[] SystemFamilies =
        {
            ("Walls", "Basic Wall"), ("Walls", "Stacked Wall"),
            ("Roofs", "Basic Roof"),
            ("Floors", "Floor"),
            ("Ceilings", "Compound Ceiling"), ("Ceilings", "Basic Ceiling"),
        };

        /// <summary>System families that DO discriminate — a rule may match these,
        /// and the architectural fabric rules deliberately do.</summary>
        private static readonly (string Category, string Family)[] KindBearingSystemFamilies =
        {
            ("Walls", "Curtain Wall"),
            ("Roofs", "Sloped Glazing"),
            ("Ducts", "Rectangular Duct"), ("Ducts", "Round Duct"),
            ("Structural Foundations", "Wall Foundation"),
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

        /// <summary>
        /// The other half of the narrowing, so it cannot quietly widen back: a rule
        /// matching a KIND-BEARING system family is allowed, and at least one shipped
        /// rule actually does it. Without this, someone could restore the strict list
        /// and delete the architectural rules, and both halves would still "pass".
        /// </summary>
        [Fact]
        public void A_Kind_Bearing_System_Family_May_Be_Matched()
        {
            var rows = ProdRows();
            var matched = new List<string>();

            foreach (var (cat, fam) in KindBearingSystemFamilies)
                foreach (var row in rows.Where(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase)))
                    if (ProdPatternMatcher.Matches((fam + " ").ToUpperInvariant(), row.Pattern))
                        matched.Add($"{fam} -> {row.Code}");

            Assert.True(matched.Count > 0,
                "No shipped rule matches a kind-bearing system family. Either the architectural "
                + "fabric rules were removed, or the catch-all list was widened back — and this "
                + "gate exists because narrowing it was a judgement call worth keeping visible.");

            // The two that caused the narrowing, named so the reason stays legible.
            Assert.Contains("Curtain Wall -> WCW", matched);
            Assert.Contains("Sloped Glazing -> RSG", matched);
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
        //  1b-ii. The architectural fabric rules resolve real names
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Walls, Roofs and Ceilings had NO rules at all — every one fell to the
        /// category default. These are the conventional build names, checked through the
        /// real resolver so a rule that stops matching fails here rather than going
        /// quietly generic on the next project.
        /// </summary>
        [Theory]
        // Verbatim from a real model
        [InlineData("Walls", "Basic Wall", "CLAY BRICK VILLAGE LARGE (295x150x130MM)", "WBK")]
        [InlineData("Walls", "Curtain Wall", "RD_Breeze Block 01 - 20X20cm", "WCW")]
        [InlineData("Walls", "Basic Wall", "Interior - 97mm Partition (1-hr)", "WPT")]
        [InlineData("Walls", "Basic Wall", "coping", "WCP")]
        // Conventional names a future project would use
        [InlineData("Walls", "Basic Wall", "230 Blockwork Rendered", "WBL")]
        [InlineData("Walls", "Basic Wall", "Concrete Block Wall 200", "WBL")]
        [InlineData("Walls", "Basic Wall", "200 RC Wall", "WRC")]
        [InlineData("Walls", "Basic Wall", "Cavity Wall 300", "WCV")]
        [InlineData("Walls", "Basic Wall", "Boundary Wall 230", "WBD")]
        [InlineData("Walls", "Basic Wall", "Parapet 230", "WPR")]
        [InlineData("Walls", "Basic Wall", "Retaining Wall 250", "RWL")]
        [InlineData("Roofs", "Basic Roof", "IT4 Corrugated Sheet", "RSH")]
        [InlineData("Roofs", "Basic Roof", "Clay Tile Roof", "RTL")]
        [InlineData("Roofs", "Basic Roof", "Asphalt Shingle Roof", "RTL")]
        [InlineData("Roofs", "Basic Roof", "200 RC Roof Slab", "RCS")]
        [InlineData("Roofs", "Sloped Glazing", "Rooflight 1200", "RSG")]
        [InlineData("Floors", "Floor", "Ground Slab 150", "FGS")]
        [InlineData("Floors", "Floor", "Hollow Pot Slab 250", "FRB")]
        [InlineData("Floors", "Floor", "50 Screed", "FSC")]
        [InlineData("Floors", "Floor", "Concrete Slab 200", "SLB")]   // pre-existing, unmoved
        [InlineData("Ceilings", "Compound Ceiling", "Suspended Ceiling 600x600", "CSU")]
        // Reclassified while writing the house standard, which needed a Floors rule
        // for steps. "stepsr 7" was on the CANNOT-RESOLVE list as a typo naming
        // nothing — but it does name steps, badly, and *Steps* reads it. Moving it
        // here rather than loosening the negative test around it: the name became
        // resolvable because a real rule was added, not because a gate was relaxed.
        [InlineData("Floors", "Floor", "stepsr 7", "FSP")]
        [InlineData("Floors", "Floor", "RC Steps - Terrazzo", "FSP")]
        [InlineData("Roofs", "Basic Roof", "Stone Coated Tile Roof", "RTL")]
        [InlineData("Ceilings", "Compound Ceiling", "Plastered Soffit", "CPL")]
        // Openings, circulation, MEP linear services — the categories where the
        // sub-distinction changes what you BUY, which is the BOQ's criterion.
        [InlineData("Doors", "Door", "Flush Door 900x2100", "DRT")]
        [InlineData("Doors", "Door", "FD30 Fire Door", "DRF")]
        [InlineData("Doors", "AD_Garage door", "4200X2700", "DRR")]
        [InlineData("Windows", "Window", "Aluminium Casement 1200", "WNA")]
        [InlineData("Windows", "Window", "uPVC Window 900", "WNU")]
        [InlineData("Windows", "Tpl Casement - Top Hung Side", "1500X1200", "WNC")]
        [InlineData("Windows", "M_Window-Awning-Double-Vertical", "600x1800", "WNW")]
        [InlineData("Curtain Panels", "RD_Breeze Block 01_Panel", "Concrete", "SBP")]
        [InlineData("Pipes", "Pipe Types", "uPVC 110 Soil", "PPV")]
        [InlineData("Pipes", "Pipe Types", "PPR PN20 25mm", "PPR")]
        [InlineData("Pipes", "Pipe Types", "Copper Tube 15mm", "PCU")]
        [InlineData("Pipes", "Pipe Types", "GI Pipe 25mm", "PGI")]
        [InlineData("Conduits", "Conduit", "PVC Conduit 20mm", "CPV")]
        [InlineData("Cable Trays", "Cable Tray", "Perforated 300mm", "CTP")]
        [InlineData("Ducts", "Rectangular Duct", "Galvanised Duct 400x200", "DGI")]
        [InlineData("Stairs", "Stair", "RC Stair Flight", "STRC")]
        [InlineData("Railings", "Railing", "MS Railing 1100", "RLM")]
        [InlineData("Railings", "Railing", "Glass Balustrade 1100", "GBL")]
        [InlineData("Structural Rebar", "Rebar", "Y12", "RBR")]
        [InlineData("Gutter", "Gutter", "uPVC Gutter 150", "GTP")]
        [InlineData("Plumbing Equipment", "Tank", "Poly Water Tank 5000L", "PEQT")]
        public void An_Architectural_Build_Resolves_To_Its_Own_Code(
            string category, string family, string type, string expected)
        {
            Assert.Equal(expected, ProdResolver.Resolve(
                family, type, category, null, ForCategory(ProdRows(), category), null, out _));
        }

        /// <summary>
        /// The half no rule can reach, stated rather than left as a silent gap. These are
        /// real type names from a delivered model: a colour, a thickness, a typo. Nothing
        /// in them says what the thing IS, so they resolve generically and SHOULD — the
        /// fix is a naming standard, not another rule.
        ///
        /// <para>If one of these ever starts resolving, a rule has become loose enough to
        /// match a name that carries no product word, which is the failure this whole
        /// pass has been about.</para>
        /// </summary>
        [Theory]
        [InlineData("Walls", "Basic Wall", "Exterior_CreamWhite_230 2")]
        [InlineData("Walls", "Basic Wall", "Exterior_BrownWhite_230")]
        [InlineData("Roofs", "Basic Roof", "Generic - 225mm")]
        [InlineData("Floors", "Floor", "IntFloor_tile_150")]
        public void A_Type_Name_That_Says_Nothing_Stays_Generic(string category, string family, string type)
        {
            ProdResolver.Resolve(family, type, category, null, ForCategory(ProdRows(), category),
                                 new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                 { ["Walls"] = "WL", ["Roofs"] = "RF", ["Floors"] = "FL" },
                                 out string source);
            Assert.False(ProdResolver.IsSpecific(source),
                $"'{type}' names a colour, a thickness or nothing at all. A rule matching it "
                + "would be matching noise.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1c. A generic default must not wear a specific product's name
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The category default is the LAST RESORT — "no rule matched". So it must not
        /// be a code that a family-specific rule also issues, or the generic and the
        /// specific become indistinguishable in the output.
        ///
        /// <para>Two shipped defaults did exactly that: Electrical Equipment defaulted
        /// to <c>DB</c> (distribution board) and Mechanical Equipment to <c>AHU</c> (air
        /// handling unit), while every other default names its CATEGORY — Doors→DR,
        /// Walls→WL, Furniture→FUR, Plumbing Equipment→PEQ. A Bosch built-in appliance
        /// therefore read as a distribution board, confidently, in a tag nobody
        /// re-reads. They are now EEQ and MEQ, following the file's own PEQ precedent,
        /// and the DB / AHU rules still fire for real boards and real AHUs.</para>
        ///
        /// <para>The map itself lives on the Revit-bound TagConfig, so what is asserted
        /// here is the half that can be: the two generic codes are issued by NO rule,
        /// and the two they replaced ARE — which is what made them wrong.</para>
        /// </summary>
        [Theory]
        [InlineData("EEQ")]
        [InlineData("MEQ")]
        public void A_Generic_Category_Default_Is_Issued_By_No_Specific_Rule(string generic)
        {
            var owners = ProdRows().Where(r => r.Code == generic).Select(r => r.ToString()).ToList();
            Assert.True(owners.Count == 0,
                $"'{generic}' is a CATEGORY DEFAULT — the code for 'no rule matched'. A specific "
                + "rule issuing it too makes the two indistinguishable: " + string.Join("; ", owners));
        }

        [Theory]
        [InlineData("DB", "Electrical Equipment")]
        [InlineData("AHU", "Mechanical Equipment")]
        public void And_The_Codes_They_Replaced_Really_Were_Specific_Products(string code, string category)
        {
            // Without this the test above asserts a distinction that may not exist. These
            // two are named by real rules, which is precisely why using them as the
            // catch-all was a confident wrong answer rather than a vague one.
            Assert.Contains(ProdRows(),
                r => r.Code == code && string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
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

        // ══════════════════════════════════════════════════════════════════════
        //  4. The four reference-only columns (PROD-3)
        // ══════════════════════════════════════════════════════════════════════
        //
        // TagConfig.LoadProdCsv reads columns 0-2 and stops. DESCRIPTION, DISCIPLINE,
        // SYSTEM and STANDARD_REF are documentation, which is defensible - but it was
        // SILENT. The VIE / ZVB / AAP discipline disagreement sat in the file because
        // nothing could ever notice it, and the same is true of every other value in
        // those columns.
        //
        // These tests do not wire the columns into the tag. Wiring DISCIPLINE into the
        // tag's discipline segment would change the tags on every existing model and is
        // a decision to take in Revit, not here. What they do is make the columns
        // CHECKABLE, so a wrong value fails a build instead of sitting there being read
        // by a human as fact.

        /// <summary>
        /// The vocabularies the tag engine actually knows, read from the shipped
        /// TAG_CONFIG file rather than restated here - a second copy of a list is a
        /// second thing to drift.
        ///
        /// <para>SYS rows quote a comma-separated category list, so this needs a real
        /// CSV split rather than String.Split(',').</para>
        /// </summary>
        private static (HashSet<string> Disc, HashSet<string> Sys) TagVocabulary()
        {
            var disc = new HashSet<string>(StringComparer.Ordinal);
            var sys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(
                         Path.Combine(DataDir(), "TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv")))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var c = SplitCsv(line);
                if (c.Count > 2 && c[0] == "DISC") disc.Add(c[2]);
                if (c.Count > 1 && c[0] == "SYS") sys.Add(c[1]);
            }
            // Instrument check: an empty vocabulary would pass every assertion below by
            // finding nothing to compare against.
            Assert.True(disc.Count > 5, "DISC vocabulary looks empty: " + disc.Count);
            Assert.True(sys.Count > 20, "SYS vocabulary looks empty: " + sys.Count);
            return (disc, sys);
        }

        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            foreach (char ch in line)
            {
                if (ch == '"') { q = !q; continue; }
                if (ch == ',' && !q) { outp.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(ch);
            }
            outp.Add(cur.ToString().Trim());
            return outp;
        }

        /// <summary>
        /// ProdRows() splits on a bare comma, which is correct only while no field is
        /// quoted. Today none is. The day a DESCRIPTION acquires a comma, every column
        /// after it shifts by one and DISCIPLINE silently becomes SYSTEM - so the
        /// parser's precondition is asserted rather than assumed.
        /// </summary>
        [Fact]
        public void The_Row_Parser_Precondition_Holds_For_This_File()
        {
            var quoted = File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv"))
                .Select((l, i) => (Line: i + 1, Text: (l ?? "").Trim()))
                .Where(x => x.Text.Length > 0 && !x.Text.StartsWith("#") && x.Text.Contains("\""))
                .Select(x => $"line {x.Line}: {x.Text}")
                .ToList();

            Assert.True(quoted.Count == 0,
                "A quoted field has appeared in STING_PROD_CODES.csv. Every test in this "
                + "class splits on a bare comma, so a quoted field shifts the columns and "
                + "DISCIPLINE is read as SYSTEM with no error anywhere. Move these tests "
                + "onto SplitCsv before adding one:\n  " + string.Join("\n  ", quoted));
        }

        /// <summary>
        /// The header row has to stay on line 1. Both readers - TagConfig.LoadProdCsv
        /// and ProdRows() below - skip exactly ONE line before they start skipping "#"
        /// comments. Put a comment block above the header and the header itself is
        /// parsed as data, minting a rule whose FAMILY_PATTERN is the literal string
        /// "FAMILY_PATTERN". Nothing errors; there is simply one more rule.
        ///
        /// <para>Written because adding the explanatory block now at the top of that
        /// file did exactly this on the first attempt.</para>
        /// </summary>
        [Fact]
        public void The_Header_Row_Is_Line_One_And_Is_Not_Read_As_Data()
        {
            var lines = File.ReadAllLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv"));
            Assert.StartsWith("PROD_CODE,CATEGORY,FAMILY_PATTERN", lines[0]);

            var leaked = ProdRows()
                .Where(r => r.Code == "PROD_CODE" || r.Pattern == "FAMILY_PATTERN")
                .Select(r => $"line {r.Line}: {r}")
                .ToList();
            Assert.True(leaked.Count == 0,
                "The CSV header is being read as a data row:\n  "
                + string.Join("\n  ", leaked));
        }

        /// <summary>
        /// DESCRIPTION is the only human-readable thing in a row, and STANDARD_REF is
        /// what an engineer checks the code against. A blank one is a row nobody can
        /// review. Both are populated on all 277 shipped rows today.
        /// </summary>
        [Fact]
        public void Every_Row_Carries_A_Description_And_A_Standard()
        {
            var thin = ProdRows()
                .Where(r => r.Description.Length == 0 || r.StandardRef.Length == 0)
                .Select(r => $"line {r.Line} {r.Code} [{r.Category}]"
                             + (r.Description.Length == 0 ? " no DESCRIPTION" : "")
                             + (r.StandardRef.Length == 0 ? " no STANDARD_REF" : ""))
                .ToList();

            Assert.True(thin.Count == 0,
                "Rows missing a reference column:\n  " + string.Join("\n  ", thin));
        }

        /// <summary>
        /// A DISCIPLINE the tag vocabulary does not declare cannot ever be wired up,
        /// and reads to a human as though it could. All 9 shipped values are known.
        /// </summary>
        [Fact]
        public void Every_Discipline_Is_A_Code_The_Tag_Vocabulary_Declares()
        {
            var vocab = TagVocabulary();
            var unknown = ProdRows()
                .Where(r => r.Discipline.Length > 0 && !vocab.Disc.Contains(r.Discipline))
                .Select(r => $"line {r.Line} {r.Code} [{r.Category}] -> {r.Discipline}")
                .Distinct()
                .ToList();

            Assert.True(unknown.Count == 0,
                "DISCIPLINE values TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv does not declare as a "
                + "DISC code:\n  " + string.Join("\n  ", unknown));
        }

        /// <summary>
        /// Six SYSTEM values the PROD table uses are not declared as SYS codes anywhere
        /// in the tag vocabulary. None is a typo - they are fire alarm, lighting, medical
        /// gas, radiation, high voltage and BMS, all real systems - so the repair is to
        /// ADD them to TAG_CONFIG, not to change the PROD rows.
        ///
        /// <para>That is not done here, because a SYS row feeds TagConfig.SysMap, which
        /// the tag pipeline reads at runtime: adding six changes what elements tag as,
        /// and that needs checking in Revit rather than asserting from a terminal. They
        /// are listed instead, so the disagreement is visible and a SEVENTH cannot be
        /// added quietly.</para>
        ///
        /// <para>THIS LIST ONLY SHRINKS. The test also fails on an entry that no longer
        /// appears, so closing one means deleting its line in the same commit.</para>
        /// </summary>
        [Fact]
        public void Every_System_Is_A_Code_The_Tag_Vocabulary_Declares()
        {
            // system -> why it is here, as of 2026-09-08
            var known = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FA"]  = "fire alarm; 7 rows, Fire Alarm Devices",
                ["LTG"] = "lighting; 8 rows, Lighting Fixtures/Devices",
                ["MGS"] = "medical gas; 21 rows, Specialty Equipment (Healthcare pack)",
                ["RAD"] = "radiation; 4 rows, Specialty Equipment (Healthcare pack)",
                ["HV"]  = "high voltage; 2 rows, Electrical Equipment",
                ["BMS"] = "building management; 1 row, Electrical Equipment",
            };

            var vocab = TagVocabulary();
            var rows = ProdRows();

            var unknown = rows
                .Where(r => r.System.Length > 0
                            && !vocab.Sys.Contains(r.System)
                            && !known.ContainsKey(r.System))
                .Select(r => $"line {r.Line} {r.Code} [{r.Category}] -> {r.System}")
                .Distinct()
                .ToList();

            Assert.True(unknown.Count == 0,
                "SYSTEM values that are neither a declared SYS code nor a recorded gap. "
                + "Add the SYS row to TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv, or record it here "
                + "with the reason:\n  " + string.Join("\n  ", unknown));

            // The list only shrinks: an entry that is now declared, or that no row uses,
            // is a claim about a file that has moved on.
            var used = new HashSet<string>(rows.Select(r => r.System), StringComparer.Ordinal);
            var stale = known.Keys
                .Where(k => vocab.Sys.Contains(k) || !used.Contains(k))
                .Select(k => k + (vocab.Sys.Contains(k)
                                      ? " is now a declared SYS code"
                                      : " is used by no PROD row"))
                .ToList();

            Assert.True(stale.Count == 0,
                "Recorded SYSTEM gaps that no longer apply - delete these lines:\n  "
                + string.Join("\n  ", stale));
        }

        /// <summary>
        /// The SYSTEM mirror of No_Prod_Code_Declares_Two_Disciplines. One code may
        /// appear in several categories, but if it names a different system in each,
        /// then whoever wires the column gets a value that depends on how somebody
        /// chose to model the thing. Zero today.
        /// </summary>
        [Fact]
        public void No_Prod_Code_Declares_Two_Systems()
        {
            var split = ProdRows()
                .Where(r => r.System.Length > 0)
                .GroupBy(r => r.Code, StringComparer.Ordinal)
                .Where(g => g.Select(r => r.System).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => $"{g.Key} -> " + string.Join(" / ", g.Select(r => $"{r.System} (line {r.Line}, {r.Category})")))
                .ToList();

            Assert.True(split.Count == 0,
                "One PROD code, two systems:\n  " + string.Join("\n  ", split));
        }

        /// <summary>
        /// NOT asserted, and worth saying why: a row's DISCIPLINE routinely differs from
        /// the discipline TAG_CONFIG assigns to its CATEGORY - 105 of 277 rows do. That
        /// is correct, not a defect. A medical gas outlet modelled as Specialty Equipment
        /// is MG, not H, and DISC_OVERRIDE exists in the same vocabulary file precisely
        /// because category does not determine discipline. An equality assertion here
        /// would have failed 105 true rows and taught the next author to weaken it.
        /// </summary>
        [Fact]
        public void Category_Does_Not_Determine_Discipline_And_The_File_Says_So()
        {
            var overrides = File.ReadAllLines(
                    Path.Combine(DataDir(), "TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv"))
                .Select(l => (l ?? "").Trim())
                .Count(l => l.StartsWith("DISC_OVERRIDE,"));

            Assert.True(overrides > 0,
                "DISC_OVERRIDE rows have gone from TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv. They "
                + "are the reason a PROD row's DISCIPLINE is allowed to differ from the "
                + "discipline its CATEGORY maps to - 105 of 277 rows do differ. Without "
                + "them the next reader has no evidence that the difference is intended, "
                + "and the obvious next move is an equality assertion that fails 105 true "
                + "rows.");
        }
    }
}
