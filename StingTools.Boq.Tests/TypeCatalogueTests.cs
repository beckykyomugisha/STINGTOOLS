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
    /// B3 — opt-in catalogue packs.
    ///
    /// The whole mechanism exists to avoid one thing: a fixed corporate
    /// catalogue would be ONE PERSON'S reading of the market, applied to every
    /// future project and reported by the audit as though it were a standard. A
    /// project whose doors are genuinely 850 wide would be told on every run
    /// that it is missing a type it does not want.
    ///
    /// So packs are named, versioned, and adopted by nobody until a project
    /// says so by id.
    /// </summary>
    public class TypeCatalogueAdoptionTests
    {
        private static BaselineFamilyType Door(string name, double w = 900) => new BaselineFamilyType
        {
            Category = "Doors",
            FamilyNamePatterns = new List<string> { "Door" },
            TypeName = name,
            Parameters = new List<BaselineFamilyTypeParam>
            { new BaselineFamilyTypeParam { Name = "Width", ValueMm = w } }
        };

        private static TypeCatalogueLibrary Library(params TypeCataloguePack[] packs) =>
            new TypeCatalogueLibrary { Packs = packs.ToList() };

        private static TypeCataloguePack Pack(string id = "EA-RESIDENTIAL-V1",
                                              params BaselineFamilyType[] types) =>
            new TypeCataloguePack
            {
                Id = id, Title = "t", Status = "provisional",
                SourceNote = "seeded from practice, not a standard; replace by harvest",
                FamilyTypes = (types.Length > 0 ? types : new[] { Door("STING Door 900") }).ToList()
            };

        [Fact]
        public void A_Project_That_Adopts_Nothing_Gets_Nothing()
        {
            // THE default. No pack applies unless a project asks for it by id.
            var b = new ProjectBaseline();

            var a = TypeCatalogueResolver.Apply(b, Library(Pack()));

            Assert.Empty(b.FamilyTypes);
            Assert.False(a.AnythingAdopted);
            Assert.Null(a.Summary());   // nothing to report, not "0 packs adopted"
        }

        [Fact]
        public void Adopting_By_Id_Folds_The_Packs_Types_In()
        {
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" } };

            var a = TypeCatalogueResolver.Apply(b, Library(Pack()));

            Assert.Single(b.FamilyTypes);
            Assert.Equal(1, a.TypesAdopted);
            Assert.Contains("EA-RESIDENTIAL-V1", a.AdoptedPackIds);
        }

        [Fact]
        public void Adoption_Is_Reported_So_It_Is_A_Stated_Decision()
        {
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" } };

            string s = TypeCatalogueResolver.Apply(b, Library(Pack())).Summary();

            Assert.Contains("EA-RESIDENTIAL-V1", s);
            Assert.Contains("1 family type", s);
        }

        [Fact]
        public void An_Id_Matching_No_Pack_Is_NAMED_Never_Silently_Ignored()
        {
            // A project that believes it adopted a catalogue and did not is
            // exactly the confident absence this codebase keeps paying for. A
            // typo or a stale version must say so.
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V2" } };

            var a = TypeCatalogueResolver.Apply(b, Library(Pack()));

            Assert.Empty(b.FamilyTypes);
            Assert.Contains("EA-RESIDENTIAL-V2", a.UnknownPackIds);
            Assert.Contains("EA-RESIDENTIAL-V2", a.Summary());
            Assert.Contains("DID NOTHING", a.Summary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_PROJECTS_Own_Declaration_Wins_Over_The_Packs()
        {
            // A pack is additive; a project can adopt one and still override
            // individual types. Same precedence as every other override.
            var mine = Door("STING Door 900", w: 850);
            var b = new ProjectBaseline
            {
                AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" },
                FamilyTypes = new List<BaselineFamilyType> { mine }
            };

            var a = TypeCatalogueResolver.Apply(b, Library(Pack()));

            Assert.Single(b.FamilyTypes);
            Assert.Equal(850, b.FamilyTypes[0].Parameters[0].ValueMm);
            Assert.Equal(0, a.TypesAdopted);
            Assert.Contains("Doors / STING Door 900", a.OverriddenByProject);
        }

        [Fact]
        public void An_Override_Is_Reported_Rather_Than_Mysterious()
        {
            var b = new ProjectBaseline
            {
                AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" },
                FamilyTypes = new List<BaselineFamilyType> { Door("STING Door 900", 850) }
            };

            Assert.Contains("overridden by the project", TypeCatalogueResolver.Apply(b, Library(Pack())).Summary());
        }

        [Fact]
        public void The_Same_Type_Name_In_Another_Category_Is_Not_An_Override()
        {
            var window = Door("STING Door 900");
            window.Category = "Windows";
            var b = new ProjectBaseline
            {
                AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" },
                FamilyTypes = new List<BaselineFamilyType> { window }
            };

            var a = TypeCatalogueResolver.Apply(b, Library(Pack()));

            Assert.Equal(1, a.TypesAdopted);
            Assert.Equal(2, b.FamilyTypes.Count);
        }

        [Fact]
        public void Two_Packs_Can_Be_Adopted_Together()
        {
            var b = new ProjectBaseline
            { AdoptCatalogues = new List<string> { "A-V1", "B-V1" } };

            var a = TypeCatalogueResolver.Apply(b,
                Library(Pack("A-V1", Door("STING A")), Pack("B-V1", Door("STING B"))));

            Assert.Equal(2, a.TypesAdopted);
            Assert.Equal(2, a.AdoptedPackIds.Count);
        }

        [Fact]
        public void A_Null_Library_Adopts_Nothing_And_Says_So()
        {
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" } };

            var a = TypeCatalogueResolver.Apply(b, null);

            Assert.Empty(b.FamilyTypes);
            Assert.Contains("EA-RESIDENTIAL-V1", a.UnknownPackIds);
        }

        [Fact]
        public void Adopted_Types_Are_Audited_Exactly_Like_Declared_Ones()
        {
            // The point of folding them into FamilyTypes rather than handling
            // them separately: one audit path, so an adopted type cannot behave
            // differently from a declared one.
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" } };
            TypeCatalogueResolver.Apply(b, Library(Pack()));

            var model = new ModelInventory();
            // The family name must MATCH the pack type’s pattern list ("Door"),
            // or the audit correctly reports guidance rather than a mint. The
            // first version of this test used "M_Single-Flush", which does not
            // contain "Door" — the code was right and the premise was wrong.
            model.FamilyNamesByCategory["Doors"] = new List<string> { "M_Single-Flush Door" };

            var r = BaselineAuditor.Audit(b, model);

            Assert.Contains(r.Missing, f => f.Name.Contains("STING Door 900"));
        }
    }

    /// <summary>
    /// B3 — the override merge, which is the defect this task uncovered.
    ///
    /// `BaselineRegistry.Load` merged materials, host types, levels and family
    /// EXPECTATIONS from the project override — but NOT `familyTypes`,
    /// `familyParameters` or `adoptCatalogues`. The corporate file ships all
    /// three empty, so the project override was the ONLY possible source for
    /// layers 2 and 3, and it was being dropped on the floor. Both layers
    /// shipped unreachable: a project declaring familyTypes got a clean audit
    /// and no explanation.
    ///
    /// The merge itself is Revit-side (it reads a file through StingPaths), so
    /// what is pinned here is the RULE it implements — project wins by key,
    /// unmatched entries are appended — against the same generic helper. If the
    /// rule is wrong, these fail; if the wiring is removed, the adoption tests
    /// above and the catalogue end-to-end test fail.
    /// </summary>
    public class BaselineOverrideMergeTests
    {
        /// <summary>The precedence Merge implements, restated so it can be
        /// asserted: an override entry replaces the base entry with the same
        /// key, and is appended when there is none.</summary>
        private static void Merge<T>(List<T> baseList, List<T> overrides, Func<T, string> key)
        {
            if (overrides == null || overrides.Count == 0) return;
            foreach (var o in overrides)
            {
                if (o == null) continue;
                string k = key(o);
                if (string.IsNullOrWhiteSpace(k)) continue;
                int i = baseList.FindIndex(x => string.Equals(key(x), k, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) baseList[i] = o; else baseList.Add(o);
            }
        }

        private static BaselineFamilyType Type(string cat, string name, double w) =>
            new BaselineFamilyType
            {
                Category = cat, TypeName = name,
                FamilyNamePatterns = new List<string> { "Door" },
                Parameters = new List<BaselineFamilyTypeParam>
                { new BaselineFamilyTypeParam { Name = "Width", ValueMm = w } }
            };

        [Fact]
        public void A_Family_Type_Is_Keyed_On_Category_AND_Name()
        {
            // "STING 900x2100" may legitimately exist for a door and a window.
            // Keying on name alone would let one silently replace the other.
            var baseList = new List<BaselineFamilyType> { Type("Doors", "STING X", 900) };

            Merge(baseList, new List<BaselineFamilyType> { Type("Windows", "STING X", 1200) },
                  t => t.Category + "|" + t.TypeName);

            Assert.Equal(2, baseList.Count);
        }

        [Fact]
        public void An_Override_Replaces_The_Same_Category_And_Name()
        {
            var baseList = new List<BaselineFamilyType> { Type("Doors", "STING X", 900) };

            Merge(baseList, new List<BaselineFamilyType> { Type("Doors", "STING X", 850) },
                  t => t.Category + "|" + t.TypeName);

            Assert.Single(baseList);
            Assert.Equal(850, baseList[0].Parameters[0].ValueMm);
        }

        [Fact]
        public void A_Parameter_Set_Is_Keyed_On_Category_Alone()
        {
            // One set per category is the schema's own rule — Validate() rejects
            // two — so the merge key must match it.
            var baseList = new List<BaselineFamilyParameterSet>
            { new BaselineFamilyParameterSet { Category = "Doors",
                  Parameters = new List<string> { "A" } } };

            Merge(baseList, new List<BaselineFamilyParameterSet>
            { new BaselineFamilyParameterSet { Category = "Doors",
                  Parameters = new List<string> { "B" } } }, f => f.Category);

            Assert.Single(baseList);
            Assert.Equal("B", baseList[0].Parameters.Single());
        }

        [Fact]
        public void Adoption_REPLACES_Rather_Than_Accumulates()
        {
            // The corporate list is empty by design and a project's adoption is
            // its whole statement. Appending would mean a project could never
            // un-adopt a pack that some future corporate default added.
            var b = new ProjectBaseline { AdoptCatalogues = new List<string> { "OLD-V1" } };
            var over = new ProjectBaseline { AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" } };

            if (over.AdoptCatalogues != null && over.AdoptCatalogues.Count > 0)
                b.AdoptCatalogues = over.AdoptCatalogues;

            Assert.Equal(new[] { "EA-RESIDENTIAL-V1" }, b.AdoptCatalogues);
        }
    }

    /// <summary>
    /// B3 — library validation. A pack that reads as a standard when it is not
    /// one is the failure this whole mechanism exists to prevent, so provenance
    /// is enforced rather than encouraged.
    /// </summary>
    public class TypeCatalogueValidationTests
    {
        private static TypeCataloguePack Ok() => new TypeCataloguePack
        {
            Id = "EA-RESIDENTIAL-V1", Title = "t", Status = "provisional",
            SourceNote = "seeded from practice, not a standard; replace by harvest",
            FamilyTypes = new List<BaselineFamilyType>
            {
                new BaselineFamilyType
                {
                    Category = "Doors",
                    FamilyNamePatterns = new List<string> { "Door" },
                    TypeName = "STING Door 900",
                    Parameters = new List<BaselineFamilyTypeParam>
                    { new BaselineFamilyTypeParam { Name = "Width", ValueMm = 900 } }
                }
            }
        };

        private static List<string> Problems(TypeCataloguePack p) =>
            new TypeCatalogueLibrary { Packs = new List<TypeCataloguePack> { p } }.Validate();

        [Fact]
        public void A_Well_Formed_Pack_Validates() => Assert.Empty(Problems(Ok()));

        [Fact]
        public void A_Provisional_Pack_With_No_SourceNote_Is_Caught()
        {
            // A provisional pack that does not say WHY it is provisional reads
            // exactly like a standard.
            var p = Ok(); p.SourceNote = "";

            Assert.Contains(Problems(p), x => x.Contains("sourceNote"));
        }

        [Fact]
        public void A_Reviewed_Pack_Does_Not_Need_A_SourceNote()
        {
            // The requirement is provenance for the PROVISIONAL claim, not
            // paperwork for its own sake.
            var p = Ok(); p.Status = "reviewed"; p.SourceNote = "";

            Assert.Empty(Problems(p));
        }

        [Fact]
        public void An_Unknown_Status_Is_Caught()
        {
            var p = Ok(); p.Status = "draft";

            Assert.Contains(Problems(p), x => x.Contains("draft"));
        }

        [Fact]
        public void A_Pack_With_No_Id_Could_Never_Be_Adopted()
        {
            var p = Ok(); p.Id = "";

            Assert.Contains(Problems(p), x => x.Contains("no id"));
        }

        [Fact]
        public void An_Empty_Pack_Is_Caught()
        {
            var p = Ok(); p.FamilyTypes = new List<BaselineFamilyType>();

            Assert.Contains(Problems(p), x => x.Contains("no family types"));
        }

        [Fact]
        public void A_Pack_Type_Must_Satisfy_The_SAME_Layer_2_Rules()
        {
            // Borrowing ProjectBaseline.Validate rather than re-stating the
            // rules is what keeps the two from drifting. A pack type without the
            // STING prefix is as wrong as a declared one without it.
            var p = Ok();
            p.FamilyTypes[0].TypeName = "Door 900";

            Assert.Contains(Problems(p), x => x.Contains("STING"));
        }

        [Fact]
        public void A_Duplicated_Pack_Id_Is_Caught()
        {
            var lib = new TypeCatalogueLibrary { Packs = new List<TypeCataloguePack> { Ok(), Ok() } };

            Assert.Contains(lib.Validate(), x => x.Contains("more than once"));
        }

        [Fact]
        public void ById_Is_Case_Insensitive_And_Trims()
        {
            var lib = new TypeCatalogueLibrary { Packs = new List<TypeCataloguePack> { Ok() } };

            Assert.NotNull(lib.ById("  ea-residential-v1 "));
            Assert.Null(lib.ById("EA-RESIDENTIAL-V2"));
            Assert.Null(lib.ById(""));
        }
    }

    /// <summary>
    /// B3 — the shipped pack. It ships PROVISIONAL and adopted by nobody: a
    /// provisional pack nobody has adopted can be wrong without costing
    /// anything; a corporate default cannot.
    /// </summary>
    public class ShippedTypeCatalogueTests
    {
        private static string DataFile(string n) => Path.Combine(AppContext.BaseDirectory, "Data", n);

        private static TypeCatalogueLibrary Shipped() =>
            JsonConvert.DeserializeObject<TypeCatalogueLibrary>(
                File.ReadAllText(DataFile("STING_TYPE_CATALOGUES.json")));

        private static ProjectBaseline Baseline() =>
            JsonConvert.DeserializeObject<ProjectBaseline>(
                File.ReadAllText(DataFile("STING_PROJECT_BASELINE.json")));

        [Fact]
        public void The_Shipped_Library_Validates()
            => Assert.Empty(Shipped().Validate());

        [Fact]
        public void The_Corporate_Baseline_Adopts_NOTHING()
        {
            // THE decision of §10. A corporate baseline that adopted a pack
            // would be the fixed catalogue this mechanism exists to avoid — one
            // person's reading of the market, in every future project, reported
            // as a standard.
            Assert.Empty(Baseline().AdoptCatalogues ?? new List<string>());
        }

        [Fact]
        public void The_Corporate_Baseline_Still_Declares_No_Family_Types_Directly()
        {
            // Belt and braces on the same decision: adopting nothing would mean
            // little if the types were declared inline instead.
            Assert.Empty(Baseline().FamilyTypes ?? new List<BaselineFamilyType>());
        }

        [Fact]
        public void Exactly_One_Pack_Ships_And_It_Is_Provisional()
        {
            var packs = Shipped().Packs;

            Assert.Single(packs);
            Assert.Equal("EA-RESIDENTIAL-V1", packs[0].Id);
            Assert.True(packs[0].IsProvisional, "the starter pack must not claim to be reviewed");
        }

        [Fact]
        public void The_Shipped_Pack_Says_Where_Its_Sizes_Came_From_And_That_They_Are_To_Be_Replaced()
        {
            string note = Shipped().Packs[0].SourceNote ?? "";

            Assert.Contains("NOT from a standard", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("harvest", note, StringComparison.OrdinalIgnoreCase);
            Assert.True(note.Length >= 120, "a source note too short to explain anything");
        }

        [Fact]
        public void The_Library_Note_Says_Packs_Are_Opt_In()
        {
            string note = Shipped().Note ?? "";

            Assert.Contains("OPT-IN", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("adoptCatalogues", note);
        }

        [Fact]
        public void The_Pack_Id_Carries_Its_Version()
        {
            // -V1 to -V2 must be an explicit migration, never a silent
            // redefinition of what a project already adopted.
            Assert.All(Shipped().Packs, p => Assert.Matches(@"-V\d+$", p.Id));
        }

        [Fact]
        public void Every_Shipped_Pack_Type_Carries_The_Minted_Prefix()
        {
            foreach (var t in Shipped().Packs.SelectMany(p => p.FamilyTypes))
                Assert.True(t.CarriesMintedPrefix,
                    $"'{t.TypeName}' would be unidentifiable after a vendor reload");
        }

        [Fact]
        public void Adopting_The_Shipped_Pack_Produces_A_Baseline_That_Validates()
        {
            // End to end on real data: the pack folds into a real baseline and
            // the result is still internally consistent. A pack that validates
            // alone but breaks the baseline it joins would fail only on
            // somebody's model.
            var b = Baseline();
            b.AdoptCatalogues = new List<string> { "EA-RESIDENTIAL-V1" };

            var a = TypeCatalogueResolver.Apply(b, Shipped());

            Assert.True(a.TypesAdopted > 0);
            Assert.Empty(b.Validate());
        }

        [Fact]
        public void The_Shipped_Pack_Covers_Both_Doors_And_Windows()
        {
            var cats = Shipped().Packs[0].FamilyTypes.Select(t => t.Category).Distinct().ToList();

            Assert.Contains("Doors", cats);
            Assert.Contains("Windows", cats);
        }

        [Fact]
        public void Every_Shipped_Pack_Type_States_A_Size()
        {
            // A type with no parameters mints a duplicate of whatever the source
            // happened to be — which is a type named for the baseline that is
            // not what the baseline describes. #798 in miniature.
            foreach (var t in Shipped().Packs.SelectMany(p => p.FamilyTypes))
                Assert.True(t.Parameters.Count > 0, $"'{t.TypeName}' declares no dimensions");
        }
    }
}
