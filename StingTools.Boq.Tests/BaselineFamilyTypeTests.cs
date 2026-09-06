using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// BASELINE LAYER 2 — family types.
    ///
    /// Layer 1 creates host types outright. Layer 2 mints a TYPE inside a
    /// family that is already loaded, and cannot conjure the family: with none
    /// loaded the entry is guidance, exactly as the family expectations report
    /// today. A baseline that claimed to create a door family would be
    /// inventing its own deliverable.
    ///
    /// The audit half is Revit-free and is what these pin. The MINT is
    /// Revit-bound and has never run — nothing in this codebase duplicates a
    /// FamilySymbol.
    /// </summary>
    public class BaselineFamilyTypeAuditTests
    {
        private static BaselineFamilyType Door(string typeName = "STING Flush Door 900x2100") =>
            new BaselineFamilyType
            {
                Category = "Doors",
                FamilyNamePatterns = new List<string> { "M_Single-Flush", "Single-Flush", "Door" },
                TypeName = typeName,
                Purpose = "Standard internal single leaf",
                Parameters = new List<BaselineTypeParameter>
                {
                    new BaselineTypeParameter { Name = "Width", ValueMm = 900 },
                    new BaselineTypeParameter { Name = "Height", ValueMm = 2100 },
                }
            };

        private static ProjectBaseline With(params BaselineFamilyType[] types) =>
            new ProjectBaseline { FamilyTypes = types.ToList() };

        private static ModelInventory Model(string family = "M_Single-Flush",
                                            string existingType = null,
                                            Dictionary<string, double> existingParams = null)
        {
            var m = new ModelInventory();
            if (family != null)
                m.FamiliesByCategory["Doors"] = new List<string> { family };
            if (existingType != null)
            {
                m.FamilyTypesByCategory["Doors"] = new List<string> { existingType };
                if (existingParams != null)
                    m.FamilyTypeParametersMm[ModelInventory.TypeKey("Doors", existingType)] = existingParams;
            }
            return m;
        }

        [Fact]
        public void A_Missing_Type_With_A_Loaded_Family_Is_Creatable()
        {
            var r = BaselineAuditor.Audit(With(Door()), Model());

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup);
            Assert.Equal(BaselineFindingKind.Missing, f.Kind);
            Assert.Equal("M_Single-Flush", f.HostFamily);
        }

        [Fact]
        public void The_Report_Names_The_Family_The_Type_Will_Be_Minted_Inside()
        {
            // A user confirming an Apply is agreeing to a change inside somebody
            // else's family. Naming it is the difference between consent and a
            // surprise.
            string report = BaselineAuditor.Report(BaselineAuditor.Audit(With(Door()), Model()));

            Assert.Contains("STING Flush Door 900x2100", report);
            Assert.Contains("M_Single-Flush", report);
        }

        [Fact]
        public void With_No_Loaded_Family_It_Is_Guidance_Never_Missing()
        {
            // Layer 2 mints a type; it cannot conjure the family. Listing this
            // as Missing would promise work Apply cannot do.
            var r = BaselineAuditor.Audit(With(Door()), Model(family: null));

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup);
            Assert.Equal(BaselineFindingKind.Guidance, f.Kind);
            Assert.Equal(0, r.MissingCount);
        }

        [Fact]
        public void A_Family_In_Another_Category_Is_Never_Substituted()
        {
            // A window family is not a door family, however well its name matches.
            var m = new ModelInventory();
            m.FamiliesByCategory["Windows"] = new List<string> { "M_Single-Flush" };

            var f = BaselineAuditor.Audit(With(Door()), m).Findings
                .Single(x => x.Group == BaselineAuditor.FamilyTypeGroup);

            Assert.Equal(BaselineFindingKind.Guidance, f.Kind);
        }

        [Fact]
        public void The_Pattern_List_Is_An_ORDERED_Preference()
        {
            // "the FIRST loaded family in that category whose name contains any
            // pattern" — in the order the patterns are declared. A model holding
            // both must get the preferred one, not whichever the collector
            // happened to return first.
            var m = new ModelInventory();
            m.FamiliesByCategory["Doors"] = new List<string> { "Generic Door", "M_Single-Flush" };

            Assert.Equal("M_Single-Flush", BaselineAuditor.ChooseHostFamily(Door(), m));
        }

        [Fact]
        public void A_Later_Pattern_Wins_When_The_Earlier_One_Is_Absent()
        {
            var m = new ModelInventory();
            m.FamiliesByCategory["Doors"] = new List<string> { "Vendor Panel Door 2024" };

            Assert.Equal("Vendor Panel Door 2024", BaselineAuditor.ChooseHostFamily(Door(), m));
        }

        [Fact]
        public void An_Existing_Type_At_The_Same_Size_Is_Present()
        {
            var r = BaselineAuditor.Audit(With(Door()),
                Model(existingType: "STING Flush Door 900x2100",
                      existingParams: new Dictionary<string, double> { { "Width", 900 }, { "Height", 2100 } }));

            Assert.Equal(BaselineFindingKind.Present,
                r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup).Kind);
        }

        [Fact]
        public void An_Existing_Type_At_A_DIFFERENT_Size_Is_A_Conflict_Not_An_Overwrite()
        {
            // The model's 800x2100 may be the deliberate one. #798 is the
            // standing reminder that a wrongly-"conforming" type is the most
            // expensive kind of wrong.
            var r = BaselineAuditor.Audit(With(Door()),
                Model(existingType: "STING Flush Door 900x2100",
                      existingParams: new Dictionary<string, double> { { "Width", 800 }, { "Height", 2100 } }));

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup);
            Assert.Equal(BaselineFindingKind.Conflict, f.Kind);
            Assert.Contains("800", f.Detail);
            Assert.Equal(0, r.MissingCount);
        }

        [Fact]
        public void A_Conflicting_Type_Is_Never_Offered_As_Something_To_Create()
        {
            var r = BaselineAuditor.Audit(With(Door()),
                Model(existingType: "STING Flush Door 900x2100",
                      existingParams: new Dictionary<string, double> { { "Width", 800 } }));

            Assert.DoesNotContain(r.Missing, f => f.Group == BaselineAuditor.FamilyTypeGroup);
        }

        [Fact]
        public void A_Feet_To_Millimetre_Round_Trip_Is_Not_A_Conflict()
        {
            // Revit stores lengths in feet, so 900 mm comes back as
            // 899.9999999. Comparing exactly would report a conflict on every
            // single type, and a report that cries wolf gets switched off.
            var r = BaselineAuditor.Audit(With(Door()),
                Model(existingType: "STING Flush Door 900x2100",
                      existingParams: new Dictionary<string, double>
                      { { "Width", 899.99999997 }, { "Height", 2100.0000001 } }));

            Assert.Equal(BaselineFindingKind.Present,
                r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup).Kind);
        }

        [Fact]
        public void An_Unreadable_Parameter_Is_Not_Reported_As_A_Difference()
        {
            // The Revit reader records nothing for a parameter it cannot read.
            // Treating that absence as 0 mm would report a fabricated conflict
            // at "0mm" on a type that is very likely correct.
            var r = BaselineAuditor.Audit(With(Door()),
                Model(existingType: "STING Flush Door 900x2100",
                      existingParams: new Dictionary<string, double> { { "Width", 900 } }));

            Assert.Equal(BaselineFindingKind.Present,
                r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup).Kind);
        }

        [Fact]
        public void A_Type_With_No_Read_Back_Values_At_All_Is_Present_Not_A_Conflict()
        {
            var r = BaselineAuditor.Audit(With(Door()), Model(existingType: "STING Flush Door 900x2100"));

            Assert.Equal(BaselineFindingKind.Present,
                r.Findings.Single(x => x.Group == BaselineAuditor.FamilyTypeGroup).Kind);
        }

        [Fact]
        public void Layer_2_Findings_Carry_Their_Own_Group_So_The_Minter_Can_Find_Them()
        {
            // The minter looks up Missing findings by Group|Name. A group string
            // spelled two ways is a silent no-op, not an error.
            var f = BaselineAuditor.Audit(With(Door()), Model()).Missing.Single();

            Assert.Equal("Family types", f.Group);
            Assert.Equal("Doors / STING Flush Door 900x2100", f.Name);
        }
    }

    /// <summary>
    /// BASELINE LAYER 2 — validation. Every one of these would otherwise
    /// surface as a per-type mint failure on somebody's live model, which is a
    /// far more expensive place to learn that a name was blank.
    /// </summary>
    public class BaselineFamilyTypeValidationTests
    {
        private static BaselineFamilyType Ok() => new BaselineFamilyType
        {
            Category = "Doors",
            FamilyNamePatterns = new List<string> { "Door" },
            TypeName = "STING Flush Door 900x2100",
            Parameters = new List<BaselineTypeParameter>
            { new BaselineTypeParameter { Name = "Width", ValueMm = 900 } }
        };

        private static List<string> Problems(BaselineFamilyType t) =>
            new ProjectBaseline { FamilyTypes = new List<BaselineFamilyType> { t } }.Validate();

        [Fact]
        public void A_Well_Formed_Family_Type_Validates()
            => Assert.Empty(Problems(Ok()));

        [Fact]
        public void A_Type_Name_Without_The_STING_Prefix_Is_Caught()
        {
            // The prefix is the CONTRACT that makes a minted type identifiable
            // after a vendor reloads their family. Without it, a type we added
            // to somebody else's family cannot be told from one of theirs.
            var t = Ok();
            t.TypeName = "Flush Door 900x2100";

            Assert.Contains(Problems(t), p => p.Contains("STING"));
        }

        [Theory]
        [InlineData("sting Flush Door 900x2100")]
        [InlineData("Sting Flush Door 900x2100")]
        [InlineData("STINGFlush Door")]
        public void The_Prefix_Is_Case_And_Space_Exact(string typeName)
        {
            // "STING " with that capitalisation and that trailing space is the
            // contract. A tool looking for minted types after a vendor reload
            // matches the literal prefix; accepting "sting " would let a type
            // through that the tool then cannot find.
            //
            // Added because the break-it pass showed that relaxing the
            // comparison to OrdinalIgnoreCase moved no test at all.
            var t = Ok();
            t.TypeName = typeName;

            Assert.Contains(Problems(t), p => p.Contains("STING"));
        }

        [Fact]
        public void A_Type_With_No_Category_Is_Caught()
        {
            var t = Ok(); t.Category = "";
            Assert.Contains(Problems(t), p => p.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void A_Type_With_No_Family_Patterns_Could_Never_Choose_A_Host()
        {
            var t = Ok(); t.FamilyNamePatterns = new List<string>();
            Assert.Contains(Problems(t), p => p.Contains("familyNamePatterns"));
        }

        [Fact]
        public void A_Patterns_List_Of_Blanks_Counts_As_None()
        {
            var t = Ok(); t.FamilyNamePatterns = new List<string> { "", "   " };
            Assert.Contains(Problems(t), p => p.Contains("familyNamePatterns"));
        }

        [Fact]
        public void A_Parameter_With_No_Name_Or_No_Value_Is_Caught()
        {
            var t = Ok();
            t.Parameters = new List<BaselineTypeParameter>
            {
                new BaselineTypeParameter { Name = "", ValueMm = 900 },
                new BaselineTypeParameter { Name = "Height", ValueMm = 0 },
            };

            var p = Problems(t);
            Assert.Contains(p, x => x.Contains("no name"));
            Assert.Contains(p, x => x.Contains("Height"));
        }

        [Fact]
        public void The_Same_Type_Declared_Twice_In_One_Category_Is_Caught()
        {
            var b = new ProjectBaseline { FamilyTypes = new List<BaselineFamilyType> { Ok(), Ok() } };

            Assert.Contains(b.Validate(), p => p.Contains("more than once"));
        }

        [Fact]
        public void The_Same_Type_Name_In_TWO_Categories_Is_Legal()
        {
            // "STING 900x2100" may legitimately exist for both a door and a
            // window. Rejecting that would be a false positive on a real model.
            var a = Ok();
            var w = Ok(); w.Category = "Windows";

            Assert.Empty(new ProjectBaseline { FamilyTypes = new List<BaselineFamilyType> { a, w } }.Validate());
        }

        [Fact]
        public void A_Broken_Layer_2_Entry_Stops_Apply_Before_Any_Model_Is_Touched()
        {
            // The audit reports baseline problems, and BaselineApplyCommand
            // refuses to run while any exist. That refusal is the whole value of
            // validating: a bad name found here costs nothing, found in a live
            // model it costs a rollback.
            var t = Ok(); t.TypeName = "no prefix";

            var r = BaselineAuditor.Audit(
                new ProjectBaseline { FamilyTypes = new List<BaselineFamilyType> { t } }, new ModelInventory());

            Assert.NotEmpty(r.BaselineProblems);
            Assert.Contains("THE BASELINE ITSELF HAS PROBLEMS", BaselineAuditor.Report(r));
        }
    }

    /// <summary>
    /// BASELINE LAYER 2 — the shipped corporate file. It ships with NO family
    /// types on purpose; see the test that says so.
    /// </summary>
    public class ShippedBaselineFamilyTypeTests
    {
        private static ProjectBaseline Shipped() =>
            JsonConvert.DeserializeObject<ProjectBaseline>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "STING_PROJECT_BASELINE.json")));

        [Fact]
        public void The_Shipped_Baseline_Still_Validates_With_The_New_Layer()
            => Assert.Empty(Shipped().Validate());

        [Fact]
        public void Family_Types_Ship_EMPTY_Because_The_Catalogue_Is_Not_Adopted()
        {
            // Sizes and naming are a project decision that needs human sign-off
            // (spec §8 D3). A corporate default here would push a type list into
            // every model that runs Apply, which is exactly the "adopted by
            // nobody" line the catalogue packs are held to.
            Assert.Empty(Shipped().FamilyTypes ?? new List<BaselineFamilyType>());
        }

        [Fact]
        public void The_Schema_Round_Trips_Through_The_Real_Serialiser()
        {
            // Newtonsoft leaves a mistyped field at its default rather than
            // throwing, so valid JSON plus a green build can still be
            // runtime-dead. This proves the new fields deserialise by the names
            // the JSON will actually use.
            var b = JsonConvert.DeserializeObject<ProjectBaseline>(@"{
                ""familyTypes"": [{
                    ""category"": ""Doors"",
                    ""familyNamePatterns"": [""M_Single-Flush""],
                    ""typeName"": ""STING Flush Door 900x2100"",
                    ""purpose"": ""Standard internal single leaf"",
                    ""parameters"": [{ ""name"": ""Width"", ""valueMm"": 900 }]
                }]}");

            var t = b.FamilyTypes.Single();
            Assert.Equal("Doors", t.Category);
            Assert.Equal("STING Flush Door 900x2100", t.TypeName);
            Assert.Equal("M_Single-Flush", t.FamilyNamePatterns.Single());
            Assert.Equal(900, t.Parameters.Single().ValueMm);
            Assert.Equal("Width", t.Parameters.Single().Name);
        }
    }
}
