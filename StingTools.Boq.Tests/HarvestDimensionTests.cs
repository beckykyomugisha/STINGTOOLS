using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Found in a real harvest, not imagined. One flat parameter list asked
    /// "Width" and "Height" of a Concrete-Rectangular-Column and got
    /// 4500 x 12000 — extents, not the 450 x 450 section sitting right beside
    /// them in b and h. The pack therefore proposed minting a column 4.5 m
    /// wide, and because a pack is DATA nothing downstream would have
    /// complained.
    /// </summary>
    public class HarvestDimensionTests
    {
        private static HarvestedType Observed(string category, string typeName,
                                              params (string name, double mm)[] parameters) =>
            new HarvestedType
            {
                Category = category, FamilyName = "F", TypeName = typeName, InstanceCount = 3,
                ParametersMm = parameters.ToDictionary(p => p.name, p => p.mm)
            };

        private static TypeCataloguePack Pack(TypeCatalogueLibrary l) => l.Packs.Single();

        // ── the rules ───────────────────────────────────────────────────────

        [Fact]
        public void A_Column_Is_Defined_By_b_And_h_Only()
        {
            Assert.Equal(new[] { "b", "h" }, HarvestDimensionRules.For("Structural Columns").ToArray());
        }

        [Fact]
        public void Width_Is_Not_A_Column_Dimension()
        {
            // The actual defect. On a column "Width" means something else.
            Assert.DoesNotContain("Width", HarvestDimensionRules.For("Structural Columns"));
            Assert.DoesNotContain("Height", HarvestDimensionRules.For("Structural Columns"));
        }

        [Fact]
        public void Width_IS_A_Foundation_Dimension()
        {
            // A footing genuinely is Width x Length x Thickness — the rule is
            // per category precisely because the same word means different
            // things in different ones.
            Assert.Contains("Width", HarvestDimensionRules.For("Structural Foundations"));
        }

        [Fact]
        public void An_Unknown_Category_Harvests_Nothing_Rather_Than_Guessing()
        {
            // Empty, not a fallback list. A guess here is silent and ends up in
            // a catalogue somebody later mints from.
            Assert.Empty(HarvestDimensionRules.For("Casework"));
            Assert.Empty(HarvestDimensionRules.For(""));
            Assert.Empty(HarvestDimensionRules.For(null));
            Assert.False(HarvestDimensionRules.IsKnown("Casework"));
        }

        // ── the builder honours them ────────────────────────────────────────

        [Fact]
        public void A_Columns_Extents_Never_Reach_The_Pack()
        {
            // Verbatim from the harvest that exposed this.
            var lib = TypeHarvestBuilder.Build(new[]
            {
                Observed("Structural Columns", "200x200 with 12.5 plaster 2",
                         ("b", 450), ("h", 450), ("Width", 4500), ("Height", 12000))
            }, "PRJ", new TypeHarvestReport());

            var ft = Pack(lib).FamilyTypes.Single();

            Assert.Equal(2, ft.Parameters.Count);
            Assert.DoesNotContain(ft.Parameters, p => p.Name == "Width");
            Assert.DoesNotContain(ft.Parameters, p => p.Name == "Height");
            Assert.Equal(450, ft.Parameters.Single(p => p.Name == "b").ValueMm);
        }

        [Fact]
        public void A_Doors_Width_And_Height_Do_Reach_The_Pack()
        {
            var lib = TypeHarvestBuilder.Build(new[]
            {
                Observed("Doors", "900 x 2100", ("Width", 900), ("Height", 2100))
            }, "PRJ", new TypeHarvestReport());

            Assert.Equal(2, Pack(lib).FamilyTypes.Single().Parameters.Count);
        }

        // ── the two reporting gaps the same harvest exposed ─────────────────

        [Fact]
        public void A_Type_With_No_Recognised_Dimension_Is_Named_Not_Dropped()
        {
            // "STING 1000" from a Window-Square Opening, "Parameters": [].
            // Minting it renames a type without defining it. It IS placed
            // practice, so it is kept and reported rather than silently binned.
            var report = new TypeHarvestReport();
            var lib = TypeHarvestBuilder.Build(new[] { Observed("Windows", "1000") }, "PRJ", report);

            Assert.Single(Pack(lib).FamilyTypes);
            Assert.Equal(1, report.TypesWithNoDimensions);
            Assert.Contains("without defining it", report.Summary());
        }

        [Fact]
        public void A_Name_That_Contradicts_Its_Own_Parameters_Is_Reported()
        {
            // Three columns all named 200x200 measured 175, 450 and 500 square.
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[]
            {
                Observed("Structural Columns", "200x200 with 12.5 plaster", ("b", 175), ("h", 175))
            }, "PRJ", report);

            Assert.Single(report.NameDisagreesWithSize);
            Assert.Contains("disagreeing with itself", report.Summary());
        }

        [Fact]
        public void A_Name_That_Matches_Its_Parameters_Is_Not_Reported()
        {
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[]
            {
                Observed("Doors", "900 x 2100", ("Width", 900), ("Height", 2100))
            }, "PRJ", report);

            Assert.Empty(report.NameDisagreesWithSize);
        }

        [Fact]
        public void Small_Numbers_In_A_Name_Are_Naming_Not_Dimensions()
        {
            // "Type 2" and "12.5 plaster" must not each raise a finding, or the
            // real cases are buried under noise.
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[]
            {
                Observed("Doors", "Flush 900 x 2100 type 2", ("Width", 900), ("Height", 2100))
            }, "PRJ", report);

            Assert.Empty(report.NameDisagreesWithSize);
        }

        [Fact]
        public void A_Vendor_Name_That_Disagrees_Is_Caught_Too()
        {
            // 1740x2595mm measuring 1500 x 2400, verbatim from the harvest.
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[]
            {
                Observed("Doors", "1740x2595mm", ("Width", 1500), ("Height", 2400))
            }, "PRJ", report);

            Assert.Single(report.NameDisagreesWithSize);
        }

        [Fact]
        public void A_Dimensionless_Type_Is_Not_Also_Reported_As_Disagreeing()
        {
            // It has no parameters to disagree WITH. Reporting both would name
            // the same type twice for one cause.
            var report = new TypeHarvestReport();
            TypeHarvestBuilder.Build(new[] { Observed("Windows", "1000") }, "PRJ", report);

            Assert.Equal(1, report.TypesWithNoDimensions);
            Assert.Empty(report.NameDisagreesWithSize);
        }
    }
}
