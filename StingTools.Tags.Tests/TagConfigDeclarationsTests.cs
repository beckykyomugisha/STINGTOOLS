// Tests for TagConfigDeclarations - the two dialects of the tag-config CSVs.
//
// The bug these exist for: the parser understood only the prose dialect, so all
// 58 healthcare families were reported "no Category declared" while their
// declarations sat in the file. The audit called them undeclared. They were not.
//
// The shipped-file tests at the bottom matter more than the synthetic ones. A
// synthetic fixture proves the parser handles a format I invented; the shipped
// files prove it handles the format this repo actually ships, which is where
// the defect lived.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagConfigDeclarationsTests
    {
        // ── prose dialect ────────────────────────────────────────────────────

        [Fact]
        public void ProseDialectAttributesTheCategoryToTheFamilyAbove()
        {
            var d = TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #7: STING - Air Terminal Tag",
                "TAG7: HVC_TAG_7_PARA_AT_TXT  •  Category: Air Terminals",
            });

            var one = Assert.Single(d);
            Assert.Equal("STING - Air Terminal Tag", one.FamilyName);
            Assert.Equal("Air Terminals", one.HostCategory);
            Assert.Equal("prose", one.Dialect);
        }

        [Fact]
        public void ProseCategoryWithNoFamilyAboveItIsIgnored()
        {
            // Not attributed to whatever came before in another file.
            Assert.Empty(TagConfigDeclarations.Parse(new[] { "Category: Air Terminals" }));
        }

        [Fact]
        public void CommentsAreSkippedInTheProseDialect()
        {
            Assert.Empty(TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #1: STING - Thing Tag",
                "# Category: Generic Models",
            }));
        }

        // ── row dialect ──────────────────────────────────────────────────────

        [Fact]
        public void RowDialectTakesTheNameFromFieldOneAndCategoryFromFieldThree()
        {
            var d = TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Clinical Room Tag,H,Rooms,1,CLN_ROOM_CLASS_TXT,Clinical room class label",
            });

            var one = Assert.Single(d);
            Assert.Equal("STING - Clinical Room Tag", one.FamilyName);
            Assert.Equal("Rooms", one.HostCategory);
            Assert.Equal("row", one.Dialect);
        }

        [Fact]
        public void ARowDoesNotInheritAProseFamilyHeader()
        {
            // A self-contained row must never be attributed to a header above it,
            // or one stray header would rewrite every row that follows.
            var d = TagConfigDeclarations.Parse(new[]
            {
                "Tag Family #1: STING - Something Else Tag",
                "TAG_FAMILY,STING - Autoclave Tag,H,Medical Equipment,1,CEQ_X,desc",
            });

            var one = Assert.Single(d);
            Assert.Equal("STING - Autoclave Tag", one.FamilyName);
        }

        [Fact]
        public void ARowDoesNotBecomeTheCurrentFamilyForLaterProseLines()
        {
            var d = TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Autoclave Tag,H,Medical Equipment,1,CEQ_X,desc",
                "Category: Doors",
            });

            var one = Assert.Single(d);
            Assert.Equal("Medical Equipment", one.HostCategory);
        }

        [Fact]
        public void ShortRowsAreIgnoredRatherThanThrowing()
        {
            Assert.Empty(TagConfigDeclarations.Parse(new[] { "TAG_FAMILY,STING - Thing Tag,H" }));
        }

        [Fact]
        public void BlankNameOrCategoryIsNotADeclaration()
        {
            Assert.Empty(TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,,H,Rooms,1,X,d",
                "TAG_FAMILY,STING - Thing Tag,H,,1,X,d",
            }));
        }

        // ── quoting ──────────────────────────────────────────────────────────

        [Fact]
        public void AQuotedCommaDoesNotShiftTheFieldPositions()
        {
            var d = TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,\"STING - Thing, Large Tag\",H,Rooms,1,X,d",
            });

            var one = Assert.Single(d);
            Assert.Equal("STING - Thing, Large Tag", one.FamilyName);
            Assert.Equal("Rooms", one.HostCategory);
        }

        [Theory]
        [InlineData("a,b,c", new[] { "a", "b", "c" })]
        [InlineData("a,\"b,c\",d", new[] { "a", "b,c", "d" })]
        [InlineData("a,\"b\"\"c\",d", new[] { "a", "b\"c", "d" })]
        [InlineData("a,,c", new[] { "a", "", "c" })]
        [InlineData("", new[] { "" })]
        public void SplitCsvHonoursQuotes(string line, string[] expected)
        {
            Assert.Equal(expected, TagConfigDeclarations.SplitCsv(line).ToArray());
        }

        [Fact]
        public void NullLinesAreNotAnError()
        {
            Assert.Empty(TagConfigDeclarations.Parse(null));
            Assert.Empty(TagConfigDeclarations.Parse(new string[] { null, "", "   " }));
        }

        // ── the shipped files ────────────────────────────────────────────────

        private static string DataDir()
        {
            // Walk up to the repo root, then into StingTools/Data.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static List<TagDeclaration> AllShipped()
        {
            string data = DataDir();
            Assert.True(data != null && Directory.Exists(data),
                        "StingTools/Data not found - the shipped-file tests cannot run blind");

            var all = new List<TagDeclaration>();
            var files = Directory.GetFiles(data, "STING_TAG_CONFIG_v5_0_*.csv");
            Assert.True(files.Length >= 8, $"expected the config set, found {files.Length} file(s)");

            foreach (var f in files)
                all.AddRange(TagConfigDeclarations.Parse(File.ReadLines(f)));
            return all;
        }

        [Fact]
        public void TheShippedConfigDeclaresBothDialects()
        {
            var all = AllShipped();

            // The regression: before the fix this was 0.
            Assert.True(all.Count(d => d.Dialect == "row") >= 100,
                        $"row-dialect declarations found: {all.Count(d => d.Dialect == "row")}");
            Assert.True(all.Count(d => d.Dialect == "prose") >= 300,
                        $"prose-dialect declarations found: {all.Count(d => d.Dialect == "prose")}");
        }

        [Fact]
        public void HealthcareFamiliesAreDeclaredAndWereNeverMissing()
        {
            var byName = AllShipped()
                .GroupBy(d => TagCategoryNameForms.NormaliseKey(d.FamilyName))
                .ToDictionary(g => g.Key, g => g.First().HostCategory, StringComparer.Ordinal);

            // Four that were about to be hand-declared into the WRONG category.
            // MgasNetwork.ClassifyRole recognises terminal units and alarm panels
            // only as Plumbing Fixtures, and zone valve boxes only as Pipe
            // Accessories, so these three are load-bearing for the MGPS solver.
            Assert.Equal("Plumbing Fixtures", byName[TagCategoryNameForms.NormaliseKey("STING - Medical Gas Terminal Unit Tag")]);
            Assert.Equal("Plumbing Fixtures", byName[TagCategoryNameForms.NormaliseKey("STING - Area Alarm Panel Tag")]);
            Assert.Equal("Pipe Accessories", byName[TagCategoryNameForms.NormaliseKey("STING - Zone Valve Box Tag")]);
            Assert.Equal("Rooms", byName[TagCategoryNameForms.NormaliseKey("STING - Clinical Room Tag")]);
        }

        [Fact]
        public void NoFamilyIsDeclaredTwiceWithDifferentCategories()
        {
            // The resolver keeps the first and warns. A clash here means the
            // config disagrees with itself and someone must pick.
            var clashes = AllShipped()
                .GroupBy(d => TagCategoryNameForms.NormaliseKey(d.FamilyName))
                .Where(g => g.Select(d => d.HostCategory)
                             .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => g.Key + " -> " + string.Join(" / ", g.Select(d => d.HostCategory).Distinct()))
                .ToList();

            Assert.True(clashes.Count == 0, "conflicting declarations:\n  " + string.Join("\n  ", clashes));
        }

        [Fact]
        public void EveryDeclaredCategoryIsAPlainName()
        {
            // Prose like "Columns - Architectural discipline" or "Sheets (ViewSheet)"
            // can never resolve to a tag category. Catching it here means a bad
            // edit fails a test instead of surfacing as a silent UNRESOLVED row.
            var prose = AllShipped()
                .Select(d => d.HostCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(c => c.Contains("(") || c.Contains("/") || c.Contains("—") ||
                            c.IndexOf("discipline", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(c => c)
                .ToList();

            Assert.True(prose.Count == 0,
                        "declarations that are prose rather than a category name:\n  " +
                        string.Join("\n  ", prose));
        }

        /// <summary>
        /// Declared, deliberately absent from the library, and due back when
        /// Create Tag Families next runs. EMPTY, and that is the normal state.
        ///
        /// <para>Nine LPS families sat here on 2026-09-21. Revit refuses to
        /// reassign an existing family's category - "the input category id
        /// cannot be assigned as the new category for this family", measured
        /// three times - so a Multi-Category tag has to be re-BORN, not
        /// converted. They were deleted, rebuilt by the creator the same
        /// evening, and removed from here because the guard below refused to
        /// let them stay.</para>
        ///
        /// <para>Names go here rather than into a lowered count floor, so a
        /// family going missing by ACCIDENT still fails. And because each
        /// name is asserted to be genuinely absent, the list cannot rot: it
        /// empties itself the moment the families come back.</para>
        /// </summary>
        private static readonly string[] AwaitingMultiCategoryRebuild =
            new string[0];

        [Fact]
        public void EveryShippedTagFamilyHasADeclaration()
        {
            // The headline gate. On 2026-09-21 this would have failed with 69
            // families - 58 hidden behind the unread row dialect, 9 behind a
            // name/file-name mismatch, 2 genuinely missing. All three causes are
            // fixed, so the number is zero and must stay zero: a new .rfa
            // dropped into the library without a declaration fails here rather
            // than surfacing months later as a silent NO-DECLARATION row.
            string data = DataDir();
            Assert.True(data != null && Directory.Exists(data), "StingTools/Data not found");

            string famDir = Path.Combine(data, "TagFamilies");
            Assert.True(Directory.Exists(famDir), "StingTools/Data/TagFamilies not found");

            var declared = new HashSet<string>(
                AllShipped().Select(d => TagCategoryNameForms.NormaliseKey(d.FamilyName)),
                StringComparer.Ordinal);

            var families = Directory.GetFiles(famDir, "*.rfa", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !IsRevitBackupName(n))
                .ToList();

            // The floor counts what the library HOLDS plus what it is knowingly
            // waiting on, so a deliberate absence does not lower the bar for an
            // accidental one.
            var present = new HashSet<string>(families, StringComparer.OrdinalIgnoreCase);

            var backAlready = AwaitingMultiCategoryRebuild.Where(present.Contains).ToList();
            Assert.True(backAlready.Count == 0,
                        $"{backAlready.Count} family/families are listed as awaiting a Multi-Category " +
                        "rebuild but are back in the library. Remove them from " +
                        "AwaitingMultiCategoryRebuild - a stale exception list hides the next real " +
                        "loss:\n  " + string.Join("\n  ", backAlready));

            int accountedFor = families.Count + AwaitingMultiCategoryRebuild.Length;
            Assert.True(accountedFor >= 200,
                        $"expected the tag library, found {families.Count} family file(s) plus " +
                        $"{AwaitingMultiCategoryRebuild.Length} awaiting rebuild = {accountedFor}");

            var missing = families
                .Where(n => !declared.Contains(TagCategoryNameForms.NormaliseKey(n)))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                        $"{missing.Count} tag families have no declared category:\n  " +
                        string.Join("\n  ", missing));
        }


        [Fact]
        public void TheThreeLpsReuseFamiliesAreMultiCategory()
        {
            // These three are the only families that legitimately serve SEVERAL
            // host categories - 15 between them, recorded in the "# Category:"
            // comment above each declaration:
            //
            //   Foundation Earth   5  Structural Foundations / Rebar / Area /
            //                         Path / Fabric Reinforcement
            //   Natural Air Term.  7  Roofs / Walls / Wall Sweeps / Curtain Wall
            //                         Mullions / Fascia / Gutter / Roof Soffits
            //   Generic Component  3  Generic Models / Specialty Equipment /
            //                         Detail Items
            //
            // BS EN 62305-3 lets an LPS REUSE existing structure - rebar as a
            // Type B foundation earth, a metal roof as a natural air
            // termination - so a single host category cannot express it. Revit's
            // answer is the Multi-Category tag, and this asserts we use it
            // rather than quietly picking one host and losing the rest.
            var byName = AllShipped()
                .GroupBy(d => TagCategoryNameForms.NormaliseKey(d.FamilyName))
                .ToDictionary(g => g.Key, g => g.First().HostCategory, StringComparer.Ordinal);

            foreach (var fam in new[]
            {
                "STING - LPS Foundation Earth (Structural Reuse) Tag",
                "STING - LPS Natural Air Termination (Architectural Reuse) Tag",
                "STING - LPS Generic Component Tag",
            })
            {
                string key = TagCategoryNameForms.NormaliseKey(fam);
                Assert.True(byName.ContainsKey(key), fam + " has no declaration");
                Assert.Equal("Multi-Category", byName[key]);
            }
        }

        [Fact]
        public void MultiCategoryResolvesToARealTagCategoryName()
        {
            // "Multi-Category" only works because FindTagCategory looks for
            // "<host> Tags" - and "Multi-Category Tags" is a real Revit
            // annotation category. If the candidate list ever stopped offering
            // it, the three LPS families would go UNRESOLVED and nothing else
            // would say why.
            Assert.Contains("Multi-Category Tags", TagCategoryNameForms.Candidates("Multi-Category"));
        }

        /// <summary>"Family.0001" - Revit's own backup, not a family.</summary>
        private static bool IsRevitBackupName(string stem)
        {
            int dot = (stem ?? "").LastIndexOf('.');
            if (dot < 0 || dot == stem.Length - 1) return false;
            string tail = stem.Substring(dot + 1);
            return tail.Length == 4 && tail.All(char.IsDigit);
        }
    }
}
