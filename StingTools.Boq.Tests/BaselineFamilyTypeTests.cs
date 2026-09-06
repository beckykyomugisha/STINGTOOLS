using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Layer 2 — types minted inside families that are already loaded.
    ///
    /// The rule that shapes every test here: a type is only MISSING when a
    /// loaded family can host it. Anything else would have Apply promise work
    /// it cannot do, which is the same honest split that reports
    /// "Structural Columns: 0 of 1 expected type(s) match" rather than
    /// pretending it can create a column family.
    /// </summary>
    public class BaselineFamilyTypeAuditTests
    {
        private static BaselineFamilyType Door(string typeName = "STING Flush Door 900x2100") =>
            new BaselineFamilyType
            {
                Category = "Doors",
                FamilyNamePatterns = { "Single-Flush", "Door" },
                TypeName = typeName,
                Purpose = "Standard internal single leaf",
                Parameters =
                {
                    new BaselineFamilyTypeParam { Name = "Width",  ValueMm = 900 },
                    new BaselineFamilyTypeParam { Name = "Height", ValueMm = 2100 }
                }
            };

        private static ProjectBaseline With(BaselineFamilyType ft)
        {
            var b = new ProjectBaseline();
            b.FamilyTypes.Add(ft);
            return b;
        }

        private static ModelInventory ModelWith(string family, params string[] types)
        {
            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Doors"] = new List<string> { family };
            inv.FamilyTypesByCategory["Doors"] = types.ToList();
            return inv;
        }

        [Fact]
        public void A_Loaded_Family_Without_The_Type_Is_Missing_And_Names_Its_Host()
        {
            var r = BaselineAuditor.Audit(With(Door()), ModelWith("M_Single-Flush", "0915 x 2134mm"));

            var f = Assert.Single(r.Missing);
            Assert.Equal("Family types", f.Group);
            Assert.Contains("M_Single-Flush", f.Detail);
            Assert.Contains("Width 900", f.Detail);
        }

        [Fact]
        public void No_Matching_Family_Is_GUIDANCE_Never_Missing()
        {
            // Apply cannot conjure a family. Listing this as Missing would
            // promise a mint that must fail.
            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Doors"] = new List<string> { "Curtain Wall Dbl Glass" };

            var r = BaselineAuditor.Audit(With(Door()), inv);

            Assert.Equal(0, r.MissingCount);
            Assert.Equal(1, r.GuidanceCount);
            Assert.Contains("CANNOT CREATE", BaselineAuditor.Report(r));
        }

        [Fact]
        public void An_Empty_Model_Is_Guidance_Too()
        {
            var r = BaselineAuditor.Audit(With(Door()), new ModelInventory());

            Assert.Equal(0, r.MissingCount);
            Assert.Equal(1, r.GuidanceCount);
        }

        [Fact]
        public void A_Family_In_Another_Category_Never_Hosts_The_Type()
        {
            // "Door" would match a window family called "Door-Height Window".
            // Category is checked first precisely so it cannot.
            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Windows"] = new List<string> { "M_Single-Flush" };

            var r = BaselineAuditor.Audit(With(Door()), inv);

            Assert.Equal(1, r.GuidanceCount);
        }

        [Fact]
        public void The_Type_Already_Present_And_Matching_Is_Present()
        {
            var inv = ModelWith("M_Single-Flush", "STING Flush Door 900x2100");
            inv.FamilyTypeParamsMm["Doors|STING Flush Door 900x2100"] =
                new Dictionary<string, double> { { "Width", 900 }, { "Height", 2100 } };

            var r = BaselineAuditor.Audit(With(Door()), inv);

            Assert.True(r.NothingToMint);
            Assert.Equal(0, r.ConflictCount);
        }

        [Fact]
        public void Same_Name_Different_Size_Is_A_CONFLICT_Never_An_Overwrite()
        {
            // The model's 800x2100 may be deliberate. Rewriting somebody's door
            // type is not a fix — #798's rule, one level down.
            var inv = ModelWith("M_Single-Flush", "STING Flush Door 900x2100");
            inv.FamilyTypeParamsMm["Doors|STING Flush Door 900x2100"] =
                new Dictionary<string, double> { { "Width", 800 }, { "Height", 2100 } };

            var r = BaselineAuditor.Audit(With(Door()), inv);

            Assert.Equal(1, r.ConflictCount);
            Assert.Equal(0, r.MissingCount);
            var f = r.Findings.Single(x => x.Kind == BaselineFindingKind.Conflict);
            Assert.Contains("800", f.Detail);
            Assert.Contains("900", f.Detail);
            Assert.Contains("Not overwritten", f.Detail);
        }

        [Fact]
        public void Sub_Millimetre_Differences_Are_Rounding_Not_A_Conflict()
        {
            // Revit stores feet. 900mm round-trips as 899.9999...
            var inv = ModelWith("M_Single-Flush", "STING Flush Door 900x2100");
            inv.FamilyTypeParamsMm["Doors|STING Flush Door 900x2100"] =
                new Dictionary<string, double> { { "Width", 899.9997 }, { "Height", 2100.0002 } };

            var r = BaselineAuditor.Audit(With(Door()), inv);

            Assert.Equal(0, r.ConflictCount);
        }

        [Fact]
        public void Uncollected_Parameters_Cannot_Manufacture_A_Conflict()
        {
            // No entry in FamilyTypeParamsMm means the values were never read.
            // Claiming a difference from missing data would be a fabrication.
            var r = BaselineAuditor.Audit(With(Door()), ModelWith("M_Single-Flush", "STING Flush Door 900x2100"));

            Assert.Equal(0, r.ConflictCount);
            Assert.Equal(1, r.PresentCount);
        }

        [Fact]
        public void The_Match_Key_Separates_Same_Named_Types_In_Different_Categories()
        {
            // Two categories can want "STING Standard 900x2100". Matching on a
            // display string would mint one and skip the other.
            var b = new ProjectBaseline();
            b.FamilyTypes.Add(Door("STING Standard 900x2100"));
            var win = Door("STING Standard 900x2100");
            win.Category = "Windows";
            // Its own patterns: the door patterns would not match a window
            // family, and the type would be guidance rather than Missing —
            // which is correct behaviour, but not what this test is about.
            win.FamilyNamePatterns = new List<string> { "Fixed" };
            b.FamilyTypes.Add(win);

            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Doors"] = new List<string> { "M_Single-Flush" };
            inv.FamilyNamesByCategory["Windows"] = new List<string> { "M_Fixed" };

            var r = BaselineAuditor.Audit(b, inv);

            Assert.Equal(2, r.MissingCount);
            Assert.Equal(2, r.Missing.Select(f => f.MatchKey).Distinct().Count());
        }
    }

    public class BaselineFamilyTypeValidationTests
    {
        private static ProjectBaseline WithType(BaselineFamilyType ft)
        {
            var b = new ProjectBaseline();
            b.FamilyTypes.Add(ft);
            return b;
        }

        [Fact]
        public void A_Type_Without_The_STING_Prefix_Is_Rejected()
        {
            // The prefix is the identification contract. A vendor reissue wipes
            // minted types; without the prefix the next audit cannot tell which
            // types were ours to re-mint.
            var b = WithType(new BaselineFamilyType
            {
                Category = "Doors", FamilyNamePatterns = { "Door" }, TypeName = "Flush Door 900x2100"
            });

            Assert.Contains(b.Validate(), p => p.Contains("STING "));
        }

        [Fact]
        public void A_Type_With_No_Family_Patterns_Is_Rejected()
        {
            var b = WithType(new BaselineFamilyType
            {
                Category = "Doors", TypeName = "STING Flush Door 900x2100"
            });

            Assert.Contains(b.Validate(), p => p.Contains("familyNamePatterns"));
        }

        [Fact]
        public void A_Nameless_Parameter_Is_Rejected()
        {
            var b = WithType(new BaselineFamilyType
            {
                Category = "Doors", FamilyNamePatterns = { "Door" },
                TypeName = "STING Flush Door 900x2100",
                Parameters = { new BaselineFamilyTypeParam { ValueMm = 900 } }
            });

            Assert.Contains(b.Validate(), p => p.Contains("parameter with no name"));
        }

        [Fact]
        public void A_Duplicated_Type_In_One_Category_Is_Rejected()
        {
            var b = new ProjectBaseline();
            for (int i = 0; i < 2; i++)
                b.FamilyTypes.Add(new BaselineFamilyType
                {
                    Category = "Doors", FamilyNamePatterns = { "Door" },
                    TypeName = "STING Flush Door 900x2100"
                });

            Assert.Contains(b.Validate(), p => p.Contains("more than once"));
        }

        [Fact]
        public void The_Same_Type_Name_In_Two_Categories_Is_Fine()
        {
            var b = new ProjectBaseline();
            foreach (string cat in new[] { "Doors", "Windows" })
                b.FamilyTypes.Add(new BaselineFamilyType
                {
                    Category = cat, FamilyNamePatterns = { "X" }, TypeName = "STING Standard 900x2100"
                });

            Assert.DoesNotContain(b.Validate(), p => p.Contains("more than once"));
        }
    }

    /// <summary>Layer 3 — the audit half. The augmenting itself is Revit-side.</summary>
    public class BaselineFamilyParameterAuditTests
    {
        private static ProjectBaseline Doors(params string[] names)
        {
            var b = new ProjectBaseline();
            b.FamilyParameters.Add(new BaselineFamilyParameterSet
            {
                Category = "Doors", Parameters = names.ToList(), IsInstance = false
            });
            return b;
        }

        [Fact]
        public void Families_Lacking_The_Parameter_Are_Counted_Not_Listed_Individually()
        {
            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Doors"] = new List<string> { "A", "B", "C" };
            inv.FamilyParamNames["A"] = new HashSet<string> { "BLE_DOOR_LEAF_MATERIAL_TXT" };

            var r = BaselineAuditor.Audit(Doors("BLE_DOOR_LEAF_MATERIAL_TXT"), inv);

            var f = Assert.Single(r.Missing);
            Assert.Contains("2 of 3", f.Detail);
        }

        [Fact]
        public void All_Families_Already_Carrying_It_Is_Present()
        {
            var inv = new ModelInventory();
            inv.FamilyNamesByCategory["Doors"] = new List<string> { "A" };
            inv.FamilyParamNames["A"] = new HashSet<string> { "BLE_DOOR_LEAF_MATERIAL_TXT" };

            var r = BaselineAuditor.Audit(Doors("BLE_DOOR_LEAF_MATERIAL_TXT"), inv);

            Assert.True(r.NothingToMint);
        }

        [Fact]
        public void No_Loaded_Families_Is_Guidance_Not_Work()
        {
            var r = BaselineAuditor.Audit(Doors("BLE_DOOR_LEAF_MATERIAL_TXT"), new ModelInventory());

            Assert.Equal(0, r.MissingCount);
            Assert.Equal(1, r.GuidanceCount);
        }

        [Fact]
        public void Instance_Parameters_Are_Rejected_By_Validation()
        {
            // The material schedule reads TYPE parameters. A per-instance value
            // would be authored and then never read.
            var b = Doors("BLE_DOOR_LEAF_MATERIAL_TXT");
            b.FamilyParameters[0].IsInstance = true;

            Assert.Contains(b.Validate(), p => p.Contains("INSTANCE"));
        }

        [Fact]
        public void An_Empty_Parameter_List_Is_Rejected()
        {
            Assert.Contains(Doors().Validate(), p => p.Contains("lists no parameters"));
        }
    }
}
