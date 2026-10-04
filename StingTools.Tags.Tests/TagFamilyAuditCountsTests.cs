// The Tag Family Audit compared a file count (210) with a category count (121) and told
// a fully loaded project it was missing families; and it called YESNO storage of the
// paragraph BOOLs "legacy" while MR_PARAMETERS.txt declares them YESNO. These pin the
// family-by-family comparison and the declared-type check.

using System;
using System.IO;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagFamilyAuditCountsTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "MR_PARAMETERS.txt")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        [Fact]
        public void A_project_holding_every_library_family_has_no_gap()
        {
            // Two families for one category: the category audit counts one, the library two.
            var lib = new[] { "STING - Door Tag", "STING - Fire Door Tag", "STING - Duct Tag" };
            var gap = TagFamilyAuditCounts.Compare(lib, lib.Concat(new[] { "M_Door Tag" }));
            Assert.Equal(3, gap.LibraryCount);
            Assert.Equal(3, gap.LoadedCount);
            Assert.Empty(gap.NotLoaded);
        }

        [Fact]
        public void Missing_families_are_named()
        {
            var gap = TagFamilyAuditCounts.Compare(
                new[] { "STING - Door Tag", "STING - Room Finish Tag", "STING - Accessible Door Tag" },
                new[] { "STING - Door Tag" });
            Assert.Equal(1, gap.LoadedCount);
            Assert.Equal(new[] { "STING - Accessible Door Tag", "STING - Room Finish Tag" }, gap.NotLoaded);
        }

        [Fact]
        public void Legacy_spellings_count_as_their_canonical_family()
        {
            const string legacy = "STING - Tie-In Point Tag (Duct — HVAC) Tag";
            const string canonical = "STING - Tie-In Point Tag (Duct — HVAC)";
            Assert.Equal(canonical, TagFamilyNameAliases.Canonicalise(legacy));

            // A library still shipping the old file beside the new one counts it once.
            var both = TagFamilyAuditCounts.Compare(new[] { legacy, canonical }, new[] { canonical });
            Assert.Equal(1, both.LibraryCount);
            Assert.Empty(both.NotLoaded);

            // A project that loaded the old name holds the family.
            var old = TagFamilyAuditCounts.Compare(new[] { canonical }, new[] { legacy });
            Assert.Empty(old.NotLoaded);
        }

        [Fact]
        public void Case_and_slash_spellings_match()
        {
            var gap = TagFamilyAuditCounts.Compare(
                new[] { "STING - Tie-In Point Tag (Conduit — Electrical LV-ELV)" },
                new[] { "sting - tie-in point tag (conduit — electrical lv/elv)" });
            Assert.Empty(gap.NotLoaded);
        }

        [Fact]
        public void Empty_inputs_give_an_empty_gap()
        {
            var gap = TagFamilyAuditCounts.Compare(null, null);
            Assert.Equal(0, gap.LibraryCount);
            Assert.Empty(gap.NotLoaded);
        }

        [Theory]
        [InlineData("YESNO", "Integer")]
        [InlineData("INTEGER", "Integer")]
        [InlineData("TEXT", "String")]
        [InlineData("text", "String")]
        [InlineData("LENGTH", null)]
        [InlineData(null, null)]
        public void Declared_type_maps_to_storage(string declared, string storage)
            => Assert.Equal(storage, TagFamilyAuditCounts.StorageForDeclaredType(declared));

        [Fact]
        public void Declared_type_is_read_from_param_rows_only()
        {
            var lines = new[]
            {
                "*PARAM\tGUID\tNAME\tDATATYPE",
                "PARAM\tabc\tTAG_PARA_STATE_1_BOOL\tYESNO\t\t1\t1\t\t1\t0",
                "PARAM\tdef\tTAG_PARA_STATE_1_BOOL_X\tTEXT\t\t1\t1\t\t1\t0",
            };
            Assert.Equal("YESNO", TagFamilyAuditCounts.DeclaredType(lines, "TAG_PARA_STATE_1_BOOL"));
            Assert.Null(TagFamilyAuditCounts.DeclaredType(lines, "TAG_PARA_STATE_2_BOOL"));
        }

        [Fact]
        public void Shipped_paragraph_bools_share_one_declared_type()
        {
            // The audit judges all ten by the first one's declared type.
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "StingTools", "Data", "MR_PARAMETERS.txt"));
            var types = Enumerable.Range(1, 10)
                .Select(i => TagFamilyAuditCounts.DeclaredType(lines, $"TAG_PARA_STATE_{i}_BOOL"))
                .ToList();
            Assert.DoesNotContain(null, types);
            Assert.Single(types.Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.NotNull(TagFamilyAuditCounts.StorageForDeclaredType(types[0]));
        }
    }
}
