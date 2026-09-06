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
    /// BASELINE LAYER 3 — shared parameters on loaded families.
    ///
    /// SHARED, not local, is the load-bearing detail.
    /// FamilyAugmentationEngine.AddTextParam takes the name/spec overload of
    /// FamilyManager.AddParameter, which creates a LOCAL family parameter: no
    /// GUID, no shared identity. Two families given "the same" parameter that
    /// way hold two unrelated parameters — they cannot be scheduled together,
    /// and the material schedule cannot read them across a project.
    ///
    /// Which is why the parameter names are validated against the real
    /// shared-parameter file BEFORE any family is opened.
    /// </summary>
    public class BaselineFamilyParameterAuditTests
    {
        private static BaselineFamilyParameterSet Doors(params string[] ps) =>
            new BaselineFamilyParameterSet
            {
                Category = "Doors",
                Parameters = (ps.Length > 0 ? ps : new[] { "BLE_DOOR_MAT_TXT", "BLE_DOOR_FRAME_MAT_TXT" }).ToList(),
                IsInstance = false,
                Group = "Materials"
            };

        private static ProjectBaseline With(params BaselineFamilyParameterSet[] sets) =>
            new ProjectBaseline { FamilyParameters = sets.ToList() };

        private static ModelInventory Model(IEnumerable<string> families = null,
                                            Dictionary<string, string[]> already = null,
                                            IEnumerable<string> uneditable = null)
        {
            var m = new ModelInventory();
            m.FamiliesByCategory["Doors"] = (families ?? new[] { "M_Single-Flush", "Vendor Door" }).ToList();
            foreach (var kv in already ?? new Dictionary<string, string[]>())
                m.FamilyParameterNames[kv.Key] = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
            foreach (string u in uneditable ?? Enumerable.Empty<string>())
                m.UneditableFamilies.Add(u);
            return m;
        }

        [Fact]
        public void Families_Lacking_The_Parameters_Are_Reported_As_Augment()
        {
            var r = BaselineAuditor.Audit(With(Doors()), Model());

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyParameterGroup);
            Assert.Equal(BaselineFindingKind.Augment, f.Kind);
            Assert.Equal(2, f.FamilyCount);
            Assert.Equal(2, r.FamiliesToAugment);
        }

        [Fact]
        public void Augmentation_Is_Counted_SEPARATELY_From_Creation()
        {
            // Editing somebody's families is a bigger act than adding a wall
            // type. A confirm dialog that folded the two together would hide the
            // bigger one inside the smaller.
            var r = BaselineAuditor.Audit(With(Doors()), Model());

            Assert.Equal(0, r.MissingCount);
            Assert.Equal(1, r.AugmentCount);
            Assert.False(r.NothingToMint);
        }

        [Fact]
        public void The_Report_Says_Augmenting_EDITS_The_Families()
        {
            string report = BaselineAuditor.Report(BaselineAuditor.Audit(With(Doors()), Model()));

            Assert.Contains("WILL AUGMENT", report);
            Assert.Contains("EDITS", report);
            Assert.Contains("BLE_DOOR_MAT_TXT", report);
        }

        [Fact]
        public void The_Report_Says_Whether_They_Are_Type_Or_Instance_Parameters()
        {
            // T6 specifies TYPE parameters; a door type's leaf material does not
            // vary per instance. A run that silently made them instance
            // parameters would schedule differently and nobody would know why.
            var typeSet = BaselineAuditor.Report(BaselineAuditor.Audit(With(Doors()), Model()));
            Assert.Contains("type parameter", typeSet, StringComparison.OrdinalIgnoreCase);

            var inst = Doors(); inst.IsInstance = true;
            Assert.Contains("INSTANCE", BaselineAuditor.Report(BaselineAuditor.Audit(With(inst), Model())));
        }

        [Fact]
        public void A_Family_That_Already_Has_Every_Parameter_Is_Not_Touched()
        {
            // Idempotent re-runs. A second Apply must not report work it will
            // not do.
            var r = BaselineAuditor.Audit(With(Doors()), Model(
                families: new[] { "M_Single-Flush" },
                already: new Dictionary<string, string[]>
                { { "M_Single-Flush", new[] { "BLE_DOOR_MAT_TXT", "BLE_DOOR_FRAME_MAT_TXT" } } }));

            Assert.Equal(0, r.AugmentCount);
            Assert.DoesNotContain(r.Findings, f => f.Group == BaselineAuditor.FamilyParameterGroup);
        }

        [Fact]
        public void A_Family_With_SOME_Of_The_Parameters_Is_Still_Augmented()
        {
            var r = BaselineAuditor.Audit(With(Doors()), Model(
                families: new[] { "M_Single-Flush" },
                already: new Dictionary<string, string[]> { { "M_Single-Flush", new[] { "BLE_DOOR_MAT_TXT" } } }));

            Assert.Equal(1, r.AugmentCount);
        }

        [Fact]
        public void An_Uneditable_Family_Is_Reported_And_Never_Attempted()
        {
            // In-place families, some vendor families, and workshared families
            // owned by another user. Expected, not exceptional.
            var r = BaselineAuditor.Audit(With(Doors()), Model(
                families: new[] { "M_Single-Flush", "In-Place Door" },
                uneditable: new[] { "In-Place Door" }));

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyParameterGroup);
            Assert.Equal(BaselineFindingKind.Augment, f.Kind);
            Assert.DoesNotContain("In-Place Door", f.Families);
            Assert.Contains("In-Place Door", f.Detail);
        }

        [Fact]
        public void When_Every_Family_Is_Uneditable_It_Is_Guidance_Not_Augment()
        {
            var r = BaselineAuditor.Audit(With(Doors()), Model(
                families: new[] { "In-Place Door" }, uneditable: new[] { "In-Place Door" }));

            var f = r.Findings.Single(x => x.Group == BaselineAuditor.FamilyParameterGroup);
            Assert.Equal(BaselineFindingKind.Guidance, f.Kind);
            Assert.Equal(0, r.AugmentCount);
        }

        [Fact]
        public void With_No_Families_Loaded_It_Is_Guidance_Never_Augment()
        {
            // Layer 3 adds parameters to families that exist. It cannot conjure
            // one, exactly as layer 2 cannot.
            var m = new ModelInventory();

            var f = BaselineAuditor.Audit(With(Doors()), m).Findings
                .Single(x => x.Group == BaselineAuditor.FamilyParameterGroup);

            Assert.Equal(BaselineFindingKind.Guidance, f.Kind);
        }

        [Fact]
        public void A_Set_With_No_Parameters_Produces_No_Finding_At_All()
        {
            var empty = Doors(); empty.Parameters = new List<string>();

            Assert.DoesNotContain(BaselineAuditor.Audit(With(empty), Model()).Findings,
                                  f => f.Group == BaselineAuditor.FamilyParameterGroup);
        }

        [Fact]
        public void The_Finding_Carries_The_Families_And_Parameters_The_Augmenter_Needs()
        {
            // The augmenter works from the finding, not by re-deriving the list.
            // Re-deriving could pick a different set than the one the user was
            // shown and agreed to.
            var f = BaselineAuditor.Audit(With(Doors()), Model()).Augments.Single();

            Assert.Equal("Family parameters", f.Group);
            Assert.Equal("Doors", f.Name);
            Assert.Equal(2, f.Parameters.Count);
            Assert.Contains("M_Single-Flush", f.Families);
        }

        [Fact]
        public void A_Model_Needing_Only_Augmentation_Does_Not_Report_Nothing_To_Do()
        {
            // NothingToMint gates whether Apply bothers asking. Leaving augments
            // out of it would make a model that needs only parameters report
            // "nothing to create" and stop.
            Assert.False(BaselineAuditor.Audit(With(Doors()), Model()).NothingToMint);
        }
    }

    /// <summary>
    /// BASELINE LAYER 3 — validation. The shared-parameter existence check is
    /// the one that matters: EditFamily + LoadFamily across every door and
    /// window family is heavy and model-mutating, and a misspelled name found
    /// halfway through it is the worst possible moment.
    /// </summary>
    public class BaselineFamilyParameterValidationTests
    {
        private static readonly HashSet<string> Known =
            new HashSet<string>(new[] { "BLE_DOOR_MAT_TXT", "BLE_DOOR_FRAME_MAT_TXT" },
                                StringComparer.OrdinalIgnoreCase);

        private static BaselineFamilyParameterSet Ok() => new BaselineFamilyParameterSet
        {
            Category = "Doors",
            Parameters = new List<string> { "BLE_DOOR_MAT_TXT" },
            Group = "Materials"
        };

        private static List<string> Problems(BaselineFamilyParameterSet fp, ISet<string> known = null) =>
            new ProjectBaseline { FamilyParameters = new List<BaselineFamilyParameterSet> { fp } }
                .Validate(known);

        [Fact]
        public void A_Well_Formed_Set_Validates()
            => Assert.Empty(Problems(Ok(), Known));

        [Fact]
        public void A_Name_That_Is_Not_In_The_Shared_Parameter_File_Is_A_BASELINE_Error()
        {
            // Caught before any model is touched. Reaching a live augment run
            // with an unresolvable name means either a silent skip or — far
            // worse — a LOCAL parameter that looks normal and cannot be
            // scheduled with its namesakes.
            var fp = Ok();
            fp.Parameters = new List<string> { "BLE_DOOR_LEAF_MATERIAL_TXT" };

            var p = Problems(fp, Known);

            Assert.Contains(p, x => x.Contains("BLE_DOOR_LEAF_MATERIAL_TXT"));
            Assert.Contains(p, x => x.IndexOf("LOCAL", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void With_No_Known_Names_Supplied_The_Existence_Check_Is_Skipped()
        {
            // An unreadable shared-parameter file must degrade to the previous
            // behaviour, not report every parameter as missing — a wall of
            // confident nonsense is worse than one missing check.
            var fp = Ok();
            fp.Parameters = new List<string> { "TOTALLY_MADE_UP" };

            Assert.Empty(Problems(fp));
        }

        [Fact]
        public void A_Set_With_No_Category_Or_No_Parameters_Is_Caught()
        {
            var noCat = Ok(); noCat.Category = "";
            Assert.Contains(Problems(noCat, Known), x => x.Contains("category"));

            var noParams = Ok(); noParams.Parameters = new List<string>();
            Assert.Contains(Problems(noParams, Known), x => x.Contains("no parameters"));
        }

        [Fact]
        public void A_Parameter_Group_The_Revit_Side_Cannot_Resolve_Is_Caught()
        {
            // The resolver would otherwise fall silently to IdentityData, and a
            // parameter in the wrong group is invisible where it was expected.
            var fp = Ok(); fp.Group = "MadeUpGroup";

            Assert.Contains(Problems(fp, Known), x => x.Contains("MadeUpGroup"));
        }

        [Theory]
        [InlineData("IdentityData")]
        [InlineData("Materials")]
        [InlineData("Construction")]
        [InlineData("Dimensions")]
        [InlineData("General")]
        [InlineData("Graphics")]
        [InlineData("Data")]
        [InlineData("Other")]
        public void Every_Declared_Group_Name_Is_Accepted(string group)
        {
            // The declared list and the Revit-side resolver must agree. A name
            // in one and not the other is a validation error at best and a
            // silent default at worst.
            var fp = Ok(); fp.Group = group;

            Assert.Empty(Problems(fp, Known));
        }

        [Fact]
        public void The_Same_Parameter_Listed_Twice_Is_Caught()
        {
            var fp = Ok();
            fp.Parameters = new List<string> { "BLE_DOOR_MAT_TXT", "BLE_DOOR_MAT_TXT" };

            Assert.Contains(Problems(fp, Known), x => x.Contains("more than once"));
        }

        [Fact]
        public void Two_Sets_For_One_Category_Are_Caught()
        {
            var b = new ProjectBaseline
            { FamilyParameters = new List<BaselineFamilyParameterSet> { Ok(), Ok() } };

            Assert.Contains(b.Validate(Known), x => x.Contains("more than once"));
        }

        [Fact]
        public void A_Broken_Layer_3_Entry_Stops_Apply_Before_Any_Family_Is_Opened()
        {
            var fp = Ok();
            fp.Parameters = new List<string> { "NOT_A_REAL_PARAM" };

            var r = BaselineAuditor.Audit(
                new ProjectBaseline { FamilyParameters = new List<BaselineFamilyParameterSet> { fp } },
                new ModelInventory(), Known);

            Assert.NotEmpty(r.BaselineProblems);
            Assert.Contains("THE BASELINE ITSELF HAS PROBLEMS", BaselineAuditor.Report(r));
        }
    }

    /// <summary>
    /// BASELINE LAYER 3 — the shared-parameter file reader. A wrong field index
    /// here would silently make every name unresolvable, which would then read
    /// as "the baseline is broken" rather than "the parser is".
    /// </summary>
    public class SharedParameterNamesTests
    {
        [Fact]
        public void A_PARAM_Row_Yields_Its_Name_From_The_Third_Field()
        {
            var names = SharedParameterNames.Parse(new[]
            {
                "# This is a Revit shared parameter file.",
                "*META\tVERSION\tMINVERSION",
                "META\t2\t1",
                "*GROUP\tID\tNAME",
                "GROUP\t1\tASS_MNG",
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE",
                "PARAM\t11111111-1111-1111-1111-111111111111\tBLE_DOOR_MAT_TXT\tTEXT\t\t1\t1",
            });

            Assert.Equal(new[] { "BLE_DOOR_MAT_TXT" }, names);
        }

        [Fact]
        public void Group_And_Meta_And_Comment_Rows_Are_Not_Parameters()
        {
            // "GROUP\t1\tASS_MNG" has a name in field 2 as well. Reading it as a
            // parameter would let a baseline naming a GROUP validate happily and
            // then find nothing at augment time.
            var names = SharedParameterNames.Parse(new[]
            {
                "GROUP\t1\tASS_MNG", "META\t2\t1", "# comment", "", "   ",
                "*PARAM\tGUID\tNAME",
            });

            Assert.Empty(names);
        }

        [Fact]
        public void A_Truncated_Row_Is_Skipped_Rather_Than_Guessed_At()
            => Assert.Empty(SharedParameterNames.Parse(new[] { "PARAM\tGUID" }));

        [Fact]
        public void Null_Input_Yields_An_Empty_Set_Not_A_Crash()
            => Assert.Empty(SharedParameterNames.Parse(null));

        [Fact]
        public void A_Missing_File_Yields_An_Empty_Set_Which_SKIPS_The_Check()
        {
            // Empty is the safe failure: Validate() skips the existence check
            // when given no names, so an unreadable file degrades to the old
            // behaviour instead of condemning every parameter.
            Assert.Empty(SharedParameterNames.ParseFile(
                Path.Combine(AppContext.BaseDirectory, "no_such_file_at_all.txt")));
        }

        [Fact]
        public void A_Missing_File_Warns_That_The_CHECK_Is_Skipped_Not_That_It_Failed()
        {
            // Both the not-found guard and the catch return an empty set, so no
            // test can tell them apart by RESULT — the break-it pass showed
            // exactly that. What differs is the message, and the message is the
            // deliverable: "the check was skipped" sends a reader somewhere
            // completely different from "the file is corrupt".
            string warned = null;
            SharedParameterNames.ParseFile(
                Path.Combine(AppContext.BaseDirectory, "no_such_file_at_all.txt"),
                m => warned = m);

            Assert.NotNull(warned);
            Assert.Contains("not found", warned, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SKIPPED", warned, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_Readable_File_Warns_About_Nothing()
        {
            string warned = null;
            SharedParameterNames.ParseFile(
                Path.Combine(AppContext.BaseDirectory, "Data", "MR_PARAMETERS.txt"), m => warned = m);

            Assert.Null(warned);
        }

        [Fact]
        public void The_Real_Shipped_File_Parses_To_Thousands_Of_Names()
        {
            var names = SharedParameterNames.ParseFile(
                Path.Combine(AppContext.BaseDirectory, "Data", "MR_PARAMETERS.txt"));

            Assert.True(names.Count > 3000, $"only {names.Count} names parsed — the format has changed");
            Assert.Contains("BLE_DOOR_MAT_TXT", names);
        }
    }

    /// <summary>
    /// BASELINE LAYER 3 — the shipped corporate file, checked against the real
    /// shared-parameter file. This is the seam the spec's own example names
    /// would have failed: BLE_DOOR_LEAF_MATERIAL_TXT and its two companions are
    /// NOT in MR_PARAMETERS.txt, so the data was fixed rather than the check
    /// loosened.
    /// </summary>
    public class ShippedBaselineFamilyParameterTests
    {
        private static string DataFile(string n) => Path.Combine(AppContext.BaseDirectory, "Data", n);

        private static ProjectBaseline Shipped() =>
            JsonConvert.DeserializeObject<ProjectBaseline>(File.ReadAllText(DataFile("STING_PROJECT_BASELINE.json")));

        private static ISet<string> Known() =>
            SharedParameterNames.ParseFile(DataFile("MR_PARAMETERS.txt"));

        [Fact]
        public void The_Shipped_Baseline_Validates_Against_The_REAL_Shared_Parameter_File()
            => Assert.Empty(Shipped().Validate(Known()));

        [Fact]
        public void Every_Shipped_Parameter_Name_Resolves()
        {
            var known = Known();
            foreach (var set in Shipped().FamilyParameters ?? new List<BaselineFamilyParameterSet>())
                foreach (string name in set.CleanParameters)
                    Assert.True(known.Contains(name),
                        $"'{name}' is declared for {set.Category} but is not in MR_PARAMETERS.txt — "
                        + "it would become a LOCAL parameter with no shared identity");
        }

        [Fact]
        public void Shipped_Sets_Are_TYPE_Parameters()
        {
            // T6 specifies type parameters. A door type's leaf material does not
            // vary per instance, and an instance parameter would schedule
            // differently.
            foreach (var set in Shipped().FamilyParameters ?? new List<BaselineFamilyParameterSet>())
                Assert.False(set.IsInstance, $"{set.Category} ships as an INSTANCE parameter set");
        }

        [Fact]
        public void Doors_And_Windows_Both_Declare_A_Material_Parameter()
        {
            // The whole point of layer 3: T6's emitter cannot read a material
            // that no family carries.
            var sets = (Shipped().FamilyParameters ?? new List<BaselineFamilyParameterSet>())
                .ToDictionary(s => s.Category, StringComparer.OrdinalIgnoreCase);

            Assert.Contains(sets["Doors"].CleanParameters, p => p.Contains("MAT"));
            Assert.Contains(sets["Windows"].CleanParameters, p => p.Contains("MAT"));
        }

        [Fact]
        public void No_Shipped_Set_Carries_A_VALUE_Mapping()
        {
            // Layer 3 creates EMPTY parameters. Deciding that a door type's leaf
            // is "flush timber, hollow core" is a human declaration; inferring
            // it from a type name is what PR #710 withdrew three rules for.
            //
            // Stated as a schema fact: BaselineFamilyParameterSet has nowhere to
            // put a value, so a corporate default is unrepresentable rather than
            // merely absent.
            Assert.DoesNotContain(typeof(BaselineFamilyParameterSet).GetProperties()
                                      .Select(x => x.Name)
                                      .Concat(typeof(BaselineFamilyParameterSet).GetFields()
                                                  .Select(x => x.Name)),
                                  n => n.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void The_Schema_Round_Trips_Through_The_Real_Serialiser()
        {
            var b = JsonConvert.DeserializeObject<ProjectBaseline>(@"{
                ""familyParameters"": [{
                    ""category"": ""Doors"",
                    ""parameters"": [""BLE_DOOR_MAT_TXT""],
                    ""isInstance"": false,
                    ""group"": ""Materials""
                }]}");

            var set = b.FamilyParameters.Single();
            Assert.Equal("Doors", set.Category);
            Assert.Equal("BLE_DOOR_MAT_TXT", set.Parameters.Single());
            Assert.False(set.IsInstance);
            Assert.Equal("Materials", set.Group);
        }
    }
}
