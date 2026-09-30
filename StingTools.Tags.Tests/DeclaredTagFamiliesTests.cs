// Families declared in the tag config are built by Create Tag Families.
//
// The defect: the creator built only the families in its own C# tables. Four
// specialist tags (Fire Door, Accessible Door, Room Finish, Fire Compartment)
// were declared, bound, preset and named by drawing types, and the button still
// said "all families already loaded". A declaration is now the request to build.
//
// The shipped-file tests hold the data to what the command needs at run time:
// a family whose label is built by hand must say which parameters it reads, and
// each of those must exist in MR_PARAMETERS.txt, or the short parameter file the
// command writes for it comes out missing lines.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DeclaredTagFamiliesTests
    {
        // ── parsing ──────────────────────────────────────────────────────────

        [Fact]
        public void ARowCarriesItsDisciplineAndItsLabelParameters()
        {
            var one = Assert.Single(TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Fire Door Tag,A,Doors,LabelMaster: FireDoor; Params: PER_SMOKE_STOP_BOOL BLE_DOOR_CLOSER_TXT,hand-built",
            }));

            Assert.Equal("A", one.Discipline);
            Assert.Equal("FireDoor", one.LabelMaster);
            Assert.Equal(new[] { "PER_SMOKE_STOP_BOOL", "BLE_DOOR_CLOSER_TXT" }, one.Params);
        }

        [Fact]
        public void NoParamsDeclaredMeansAnEmptyListNotNull()
        {
            var one = Assert.Single(TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - Clinical Room Tag,H,Rooms,1,CLN_ROOM_CLASS_TXT,Clinical room class label",
            }));
            Assert.NotNull(one.Params);
            Assert.Empty(one.Params);
        }

        [Fact]
        public void ParamsStopAtTheEndOfTheField()
        {
            // The description after the next comma must not be read as names.
            var one = Assert.Single(TagConfigDeclarations.Parse(new[]
            {
                "TAG_FAMILY,STING - X Tag,A,Rooms,Params: A_TXT B_TXT,SEE ALSO C_TXT",
            }));
            Assert.Equal(new[] { "A_TXT", "B_TXT" }, one.Params);
        }

        // ── merge / not built ────────────────────────────────────────────────

        private static TagDeclaration D(string name, string cat, string master = "universal", params string[] ps)
            => new TagDeclaration { FamilyName = name, HostCategory = cat, LabelMaster = master, Params = ps.ToList() };

        [Fact]
        public void TwinDeclarationsMergeIntoOneWithTheUnionOfParameters()
        {
            var merged = DeclaredTagFamilies.Merge(new[]
            {
                D("STING - Fire Door Tag", "Doors", "universal", "A_TXT"),
                D("STING - Fire Door Tag", "Doors", "FireDoor", "A_TXT", "B_TXT"),
            });

            var one = Assert.Single(merged);
            Assert.Equal("FireDoor", one.LabelMaster);   // a named master wins over the default
            Assert.Equal(new[] { "A_TXT", "B_TXT" }, one.Params);
        }

        [Fact]
        public void FamiliesTheCreatorAlreadyBuildsAreNotBuiltTwice()
        {
            var left = DeclaredTagFamilies.NotBuiltBy(
                new[]
                {
                    D("STING - Door Tag", "Doors"),
                    // declared without the trailing " Tag" the creator's name carries
                    D("STING - Tie-In Point Tag (Duct — HVAC)", "Ducts"),
                    D("STING - Fire Door Tag", "Doors", "FireDoor"),
                },
                new[] { "STING - Door Tag", "STING - Tie-In Point Tag (Duct — HVAC) Tag" });

            Assert.Equal(new[] { "STING - Fire Door Tag" }, left.Select(d => d.FamilyName));
        }

        [Fact]
        public void MastersAreDeclaredButNeverMinted()
        {
            var left = DeclaredTagFamilies.NotBuiltBy(
                new[] { D("STING_LPS_Tag_Universal", "Multi-Category", "LPS") }, new string[0]);
            Assert.Empty(left);
        }

        [Theory]
        [InlineData("2.5", "TXT_2_5")]
        [InlineData("3.5", "TXT_3_5")]
        [InlineData("2", "TXT_2_0")]
        [InlineData("3.5mm", "TXT_3_5")]
        [InlineData("abc", null)]
        [InlineData("", null)]
        public void SizeSwitchNamesMatchTheBuildSheet(string size, string expected)
            => Assert.Equal(expected, DeclaredTagFamilies.LabelSizeSwitchName(size));

        [Fact]
        public void FileNamesCannotCarryASlash()
            => Assert.Equal("STING - Brace - Truss Tag.rfa", DeclaredTagFamilies.FileName("STING - Brace / Truss Tag"));

        // ── shared-parameter subset ──────────────────────────────────────────

        private static readonly string[] SampleFile =
        {
            "# This is a Revit shared parameter file.",
            "*META\tVERSION\tMINVERSION",
            "META\t2\t1",
            "*GROUP\tID\tNAME",
            "GROUP\t1\tASS_MNG",
            "GROUP\t2\tCST_PROC",
            "GROUP\t3\tBLE",
            "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEONNOCATEGORY",
            "PARAM\taaaa\tA_TXT\tTEXT\t\t1\t1\t\t1\t0",
            "# v3.9 | history line in the body",
            "PARAM\tbbbb\tB_TXT\tTEXT\t\t2\t1\t\t1\t0",
            "PARAM\tcccc\tC_BOOL\tYESNO\t\t3\t1\t\t1\t0",
        };

        [Fact]
        public void TheSubsetKeepsOnlyTheNamedParametersAndTheirGroups()
        {
            var r = SharedParamSubset.Build(SampleFile, new[] { "C_BOOL", "A_TXT" });

            Assert.Empty(r.Missing);
            Assert.Equal(new[]
            {
                "# This is a Revit shared parameter file.",
                "*META\tVERSION\tMINVERSION",
                "META\t2\t1",
                "*GROUP\tID\tNAME",
                "GROUP\t1\tASS_MNG",
                "GROUP\t3\tBLE",
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEONNOCATEGORY",
                "PARAM\taaaa\tA_TXT\tTEXT\t\t1\t1\t\t1\t0",
                "PARAM\tcccc\tC_BOOL\tYESNO\t\t3\t1\t\t1\t0",
            }, r.Lines);
        }

        [Fact]
        public void AParameterTheFileDoesNotDefineIsReportedMissing()
        {
            var r = SharedParamSubset.Build(SampleFile, new[] { "A_TXT", "NOPE_TXT" });
            Assert.Equal(new[] { "NOPE_TXT" }, r.Missing);
        }

        // ── the shipped config ───────────────────────────────────────────────

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "StingTools/Data not found");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static List<TagDeclaration> DeclaredOnlyShipped()
        {
            var all = new List<TagDeclaration>();
            foreach (var f in Directory.GetFiles(DataDir(), "STING_TAG_CONFIG_v5_0_*.csv"))
                all.AddRange(TagConfigDeclarations.Parse(File.ReadLines(f)));
            return DeclaredTagFamilies.NotBuiltBy(all, DrawingTypeTagFamilyGateTests.BuiltInCreatorFamilyNames());
        }

        [Fact]
        public void TheSpecialistFamiliesAreBuiltFromTheConfig()
        {
            var names = DeclaredOnlyShipped().Select(d => d.FamilyName).ToList();
            foreach (var want in new[]
            {
                "STING - Fire Door Tag", "STING - Accessible Door Tag",
                "STING - Room Finish Tag", "STING - Fire Compartment Tag",
            })
                Assert.Contains(want, names);
        }

        [Fact]
        public void NoFamilyIsDeclaredForACategoryRevitCannotMakeATagFor()
        {
            // Measured in Revit 2025, 2026-09-30: no Curtain System Tag template is
            // installed, and Revit refuses to move a Generic Tag family into Curtain
            // System Tags ("The input category id cannot be assigned as the new
            // category for this family"). A declaration for it fails on every run.
            var unbuildable = new[] { "Curtain Systems" };
            var bad = DeclaredOnlyShipped()
                .Where(d => unbuildable.Contains(d.HostCategory, StringComparer.OrdinalIgnoreCase))
                .Select(d => d.FamilyName).ToList();
            Assert.True(bad.Count == 0, "declared for a category Revit cannot tag this way: " + string.Join(", ", bad));
        }

        [Fact]
        public void EveryHandBuiltFamilyDeclaresParametersThatExist()
        {
            var defined = new HashSet<string>(
                File.ReadLines(Path.Combine(DataDir(), "MR_PARAMETERS.txt"))
                    .Where(l => l.StartsWith("PARAM\t"))
                    .Select(l => l.Split('\t')[2]),
                StringComparer.Ordinal);

            var handBuilt = DeclaredOnlyShipped().Where(d => !d.Universal).ToList();
            Assert.NotEmpty(handBuilt);   // guards the loop below from passing vacuously

            var problems = new List<string>();
            foreach (var d in handBuilt)
            {
                if (d.Params.Count == 0)
                    problems.Add($"{d.FamilyName}: no 'Params:' declared - its parameter file would be empty");
                foreach (var p in d.Params.Where(p => !defined.Contains(p)))
                    problems.Add($"{d.FamilyName}: {p} is not in MR_PARAMETERS.txt");
                if (string.IsNullOrWhiteSpace(d.Discipline))
                    problems.Add($"{d.FamilyName}: no discipline - its size types would use the generic style");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
