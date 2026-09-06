using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Harvest — the mechanism that lets the catalogue grow from work that was
    /// built rather than from anyone's recollection of the market.
    ///
    /// The rule that shapes it: PLACED types only. A type sitting in the
    /// browser that nobody used is evidence that somebody loaded a family, not
    /// evidence of practice, and counting it would let an unused vendor library
    /// masquerade as a standard.
    /// </summary>
    public class TypeHarvestTests
    {
        private static HarvestedType Placed(string type, int count, string family = "M_Single-Flush") =>
            new HarvestedType
            {
                Category = "Doors", FamilyName = family, TypeName = type, InstanceCount = count,
                ParametersMm = new Dictionary<string, double> { { "Width", 900 }, { "Height", 2100 } }
            };

        private static TypeCataloguePack Pack(TypeCatalogueLibrary lib) => lib.Packs.Single();

        [Fact]
        public void A_Placed_Type_Is_Harvested()
        {
            var report = new TypeHarvestReport();
            var lib = TypeHarvestBuilder.Build(new[] { Placed("0900 x 2100mm", 14) }, "PRJ01", report);

            Assert.Equal(1, report.TypesHarvested);
            var ft = Pack(lib).FamilyTypes.Single();
            Assert.Equal("STING 0900 x 2100mm", ft.TypeName);
            Assert.Contains("14 instance(s)", ft.Purpose);
            Assert.Equal(2, ft.Parameters.Count);
        }

        [Fact]
        public void A_Type_Nobody_Placed_Is_Not_Harvested()
        {
            var report = new TypeHarvestReport();
            var lib = TypeHarvestBuilder.Build(new[] { Placed("Unused Type", 0) }, "PRJ01", report);

            Assert.Empty(Pack(lib).FamilyTypes);
            Assert.Equal(1, report.SkippedNotPlaced);
            Assert.Contains("not evidence of practice", report.Summary());
        }

        [Fact]
        public void Our_Own_Minted_Types_Are_Not_Harvested_Back()
        {
            // Otherwise the catalogue becomes a record of itself: mint from a
            // pack, harvest, and the pack reappears as though the project chose
            // those sizes.
            var report = new TypeHarvestReport();
            var lib = TypeHarvestBuilder.Build(new[] { Placed("STING Flush Door 900x2100", 9) },
                                               "PRJ01", report);

            Assert.Empty(Pack(lib).FamilyTypes);
            Assert.Equal(1, report.SkippedAlreadyPrefixed);
            Assert.Contains("record of itself", report.Summary());
        }

        [Fact]
        public void The_Harvested_Pack_Passes_Baseline_Validation()
        {
            // A pack that cannot be minted from is a pack that reads well and
            // does nothing. Prefixing exists so the harvest is directly usable.
            var lib = TypeHarvestBuilder.Build(new[] { Placed("0900 x 2100mm", 3) },
                                               "PRJ01", new TypeHarvestReport());

            var b = new ProjectBaseline();
            b.FamilyTypes.AddRange(Pack(lib).FamilyTypes);

            Assert.Empty(b.Validate());
            Assert.Empty(lib.Validate());
        }

        [Fact]
        public void The_Family_Name_Is_The_Only_Pattern_Used()
        {
            // Inferring "Door" from "M_Single-Flush" would match families the
            // observation never saw.
            var lib = TypeHarvestBuilder.Build(new[] { Placed("0900 x 2100mm", 3, "Vendor_ACME_Door_v4") },
                                               "PRJ01", new TypeHarvestReport());

            var ft = Pack(lib).FamilyTypes.Single();
            Assert.Equal(new[] { "Vendor_ACME_Door_v4" }, ft.FamilyNamePatterns.ToArray());
        }

        [Fact]
        public void The_Harvested_Pack_Is_Provisional_And_Adopted_By_Nobody()
        {
            var lib = TypeHarvestBuilder.Build(new[] { Placed("0900 x 2100mm", 3) },
                                               "PRJ01", new TypeHarvestReport());

            Assert.True(Pack(lib).IsProvisional);
            Assert.Contains("not a standard", Pack(lib).SourceNote);
            Assert.Contains("nothing here applies", lib.Note.ToLowerInvariant());
        }

        [Fact]
        public void The_Pack_Id_Carries_The_Project_And_A_Version()
        {
            var lib = TypeHarvestBuilder.Build(new[] { Placed("A", 1) }, "kut-01", new TypeHarvestReport());

            Assert.Equal("HARVEST-KUT-01-V1", Pack(lib).Id);
        }

        [Fact]
        public void An_Unnamed_Project_Still_Produces_A_Valid_Id()
        {
            var lib = TypeHarvestBuilder.Build(new[] { Placed("A", 1) }, null, new TypeHarvestReport());

            Assert.Equal("HARVEST-PROJECT-V1", Pack(lib).Id);
        }

        [Fact]
        public void Nothing_Observed_Says_So_Rather_Than_Producing_An_Empty_Pack()
        {
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new HarvestedType[0], "PRJ01", report);

            Assert.Equal(0, report.TypesObserved);
            Assert.Contains("Nothing to harvest", report.Summary());
        }

        [Fact]
        public void Zero_Valued_Parameters_Are_Dropped()
        {
            // A parameter that reads 0 was not set. Recording it would propose
            // minting a door 0mm wide.
            var h = Placed("0900 x 2100mm", 2);
            h.ParametersMm["Depth"] = 0;

            var lib = TypeHarvestBuilder.Build(new[] { h }, "PRJ01", new TypeHarvestReport());

            Assert.DoesNotContain(Pack(lib).FamilyTypes.Single().Parameters, p => p.Name == "Depth");
        }

        [Fact]
        public void Counts_Are_Reported_Per_Category()
        {
            var win = Placed("1200 x 1200mm", 6);
            win.Category = "Windows";

            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[] { Placed("0900 x 2100mm", 4), win }, "PRJ01", report);

            Assert.Equal(1, report.ByCategory["Doors"]);
            Assert.Equal(1, report.ByCategory["Windows"]);
            Assert.Contains("Doors 1", report.Summary());
        }
    }
}
