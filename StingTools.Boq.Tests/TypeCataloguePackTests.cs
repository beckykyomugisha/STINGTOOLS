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
    /// Catalogue packs. The property that matters most is the one that is easy
    /// to lose: NO pack applies unless a project asks for it by id. A pack that
    /// leaked into every project would be exactly the confident corporate
    /// default the pack mechanism exists to avoid.
    /// </summary>
    public class CatalogueAdoptionTests
    {
        private static BaselineFamilyType Door(string name, double w) => new BaselineFamilyType
        {
            Category = "Doors",
            FamilyNamePatterns = { "Door" },
            TypeName = name,
            Parameters = { new BaselineFamilyTypeParam { Name = "Width", ValueMm = w } }
        };

        private static TypeCatalogueLibrary Library() => new TypeCatalogueLibrary
        {
            Packs =
            {
                new TypeCataloguePack
                {
                    Id = "EA-RESIDENTIAL-V1",
                    SourceNote = new string('x', 60),
                    FamilyTypes = { Door("STING Flush Door 900x2100", 900),
                                    Door("STING Flush Door 800x2100", 800) }
                }
            }
        };

        [Fact]
        public void A_Pack_Applies_To_NOBODY_By_Default()
        {
            // The single most important behaviour here.
            var b = new ProjectBaseline();

            var r = CatalogueAdopter.Adopt(b, Library());

            Assert.Empty(b.FamilyTypes);
            Assert.Equal(0, r.TypesAdded);
            Assert.Null(r.Summary());
        }

        [Fact]
        public void Adopting_By_Id_Brings_Its_Types_In()
        {
            var b = new ProjectBaseline();
            b.AdoptCatalogues.Add("EA-RESIDENTIAL-V1");

            var r = CatalogueAdopter.Adopt(b, Library());

            Assert.Equal(2, b.FamilyTypes.Count);
            Assert.Equal(2, r.TypesAdded);
            Assert.Contains("EA-RESIDENTIAL-V1", r.Summary());
        }

        [Fact]
        public void An_Unknown_Pack_Id_Is_Reported_Never_Silent()
        {
            // Silently ignoring a typo leaves somebody believing they adopted a
            // catalogue they did not, and wondering why nothing appeared.
            var b = new ProjectBaseline();
            b.AdoptCatalogues.Add("EA-RESIDENTAIL-V1");   // transposed

            var r = CatalogueAdopter.Adopt(b, Library());

            Assert.Empty(b.FamilyTypes);
            Assert.Single(r.Unknown);
            Assert.Contains("do not exist", r.Summary());
        }

        [Fact]
        public void A_Project_Type_Wins_Over_The_Pack()
        {
            // Adopting a pack must never take away a project's ability to differ
            // from it. This project's door is 850, and it stays 850.
            var b = new ProjectBaseline();
            b.FamilyTypes.Add(Door("STING Flush Door 900x2100", 850));
            b.AdoptCatalogues.Add("EA-RESIDENTIAL-V1");

            var r = CatalogueAdopter.Adopt(b, Library());

            Assert.Equal(2, b.FamilyTypes.Count);           // 1 project + 1 from the pack
            Assert.Equal(1, r.TypesOverriddenByProject);
            Assert.Equal(850, b.FamilyTypes.Single(t => t.TypeName == "STING Flush Door 900x2100")
                                            .Parameters[0].ValueMm);
        }

        [Fact]
        public void The_Same_Type_Name_In_Another_Category_Is_Not_An_Override()
        {
            var b = new ProjectBaseline();
            var win = Door("STING Flush Door 900x2100", 900);
            win.Category = "Windows";
            b.FamilyTypes.Add(win);
            b.AdoptCatalogues.Add("EA-RESIDENTIAL-V1");

            var r = CatalogueAdopter.Adopt(b, Library());

            Assert.Equal(0, r.TypesOverriddenByProject);
            Assert.Equal(3, b.FamilyTypes.Count);
        }

        [Fact]
        public void Adopting_The_Same_Pack_Twice_Does_Not_Duplicate()
        {
            var b = new ProjectBaseline();
            b.AdoptCatalogues.Add("EA-RESIDENTIAL-V1");
            b.AdoptCatalogues.Add("EA-RESIDENTIAL-V1");

            CatalogueAdopter.Adopt(b, Library());

            Assert.Equal(2, b.FamilyTypes.Count);
        }
    }

    public class CatalogueValidationTests
    {
        [Fact]
        public void A_Provisional_Pack_Must_Say_Where_Its_Numbers_Came_From()
        {
            // "industry standard" explains nothing. A catalogue that cannot say
            // where its sizes came from is a guess in a standard's clothes.
            var lib = new TypeCatalogueLibrary
            {
                Packs = { new TypeCataloguePack { Id = "X-V1", SourceNote = "common sizes",
                          FamilyTypes = { new BaselineFamilyType { TypeName = "STING A" } } } }
            };

            Assert.Contains(lib.Validate(), p => p.Contains("sourceNote"));
        }

        [Fact]
        public void An_Empty_Pack_Is_Rejected()
        {
            var lib = new TypeCatalogueLibrary
            {
                Packs = { new TypeCataloguePack { Id = "X-V1", SourceNote = new string('x', 60) } }
            };

            Assert.Contains(lib.Validate(), p => p.Contains("no family types"));
        }

        [Fact]
        public void A_Duplicated_Pack_Id_Is_Rejected()
        {
            var lib = new TypeCatalogueLibrary();
            for (int i = 0; i < 2; i++)
                lib.Packs.Add(new TypeCataloguePack
                {
                    Id = "X-V1", SourceNote = new string('x', 60),
                    FamilyTypes = { new BaselineFamilyType { TypeName = "STING A" } }
                });

            Assert.Contains(lib.Validate(), p => p.Contains("more than once"));
        }
    }

    /// <summary>The shipped catalogue, against the rules that consume it.</summary>
    public class ShippedCatalogueTests
    {
        private static TypeCatalogueLibrary Shipped() =>
            JsonConvert.DeserializeObject<TypeCatalogueLibrary>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Data", "STING_TYPE_CATALOGUES.json")));

        [Fact]
        public void The_Shipped_Catalogue_Validates() => Assert.Empty(Shipped().Validate());

        [Fact]
        public void Every_Shipped_Pack_Is_Provisional()
        {
            // Nothing ships as an adopted standard. The starter pack is a shape,
            // to be replaced by harvesting a delivered model.
            Assert.All(Shipped().Packs, p => Assert.True(p.IsProvisional));
        }

        [Fact]
        public void Every_Shipped_Type_Passes_Baseline_Validation()
        {
            // A pack type is a baseline type: it needs the STING prefix, family
            // patterns and named parameters, or it will fail at mint time on
            // somebody's project rather than here.
            var b = new ProjectBaseline();
            foreach (var pack in Shipped().Packs)
                b.FamilyTypes.AddRange(pack.FamilyTypes);

            Assert.Empty(b.Validate());
        }

        [Fact]
        public void No_Shipped_Pack_Is_Adopted_By_The_Corporate_Baseline()
        {
            // The corporate baseline adopting its own pack would make it the
            // corporate default this whole mechanism exists to avoid.
            var baseline = JsonConvert.DeserializeObject<ProjectBaseline>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Data", "STING_PROJECT_BASELINE.json")));

            Assert.Empty(baseline.AdoptCatalogues ?? new List<string>());
        }
    }
}
