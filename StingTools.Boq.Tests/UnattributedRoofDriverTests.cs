using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Two zeros that mean opposite things.
    ///
    /// The 20:14 export said the roof-fastener driver was zero and added "that
    /// is the intended behaviour — with no driver the quantity would be
    /// invented, not estimated." True of a model with no roof. That model had
    /// 856 m² of roof: measured, present in the schedule, and unattributable
    /// because `Generic - 225mm` matches no supplier pattern.
    ///
    /// The quantity must still NOT be derived — 11 fasteners/m² is corrugated
    /// sheeting at every second corrugation, and the roof carries Eagle
    /// high-profile tiles, which take clips at another rate. What has to change
    /// is the sentence: nothing-to-do versus name-the-type.
    /// </summary>
    public class UnattributedRoofDriverTests
    {
        private static SupplierUnitTable Table() => new SupplierUnitTable
        {
            Rules =
            {
                new SupplierUnitRule
                {
                    CommodityKey = "roof-sheet",
                    MatchCategories = new List<string> { "Roofs" },
                    MatchTypePatterns = new List<string> { "IT4", "Corrugated" },
                    // Which commodities feed a driver is DATA now, not a
                    // hardcoded key list in ConsumableDrivers. A rule that
                    // declares nothing feeds nothing — which is the point.
                    FeedsDriver = "roof_covering_m2"
                }
            }
        };

        private static ConstituentInput Roof(string typeName, double m2, string unit = "m2") =>
            new ConstituentInput
            {
                ConstituentKind = "", Category = "Roofs",
                TypeName = typeName, Quantity = m2, Unit = unit
            };

        [Fact]
        public void An_Unnameable_Roof_Does_Not_Feed_The_Fastener_Driver()
        {
            var d = ConsumableDrivers.From(new[] { Roof("Generic - 225mm", 610.61) }, Table());

            Assert.Equal(0, d.RoofCoveringM2);
        }

        [Fact]
        public void But_Its_Area_Is_Remembered_As_Unattributed()
        {
            var d = ConsumableDrivers.From(new[] { Roof("Generic - 225mm", 610.61) }, Table());

            Assert.Equal(610.61, d.RoofCoveringUnattributedM2, 2);
        }

        [Fact]
        public void A_Roof_That_Matches_A_Pattern_Feeds_The_Driver_And_Is_Not_Unattributed()
        {
            var d = ConsumableDrivers.From(new[] { Roof("IT4 Corrugated 0.5mm", 200) }, Table());

            Assert.Equal(200, d.RoofCoveringM2, 2);
            Assert.Equal(0, d.RoofCoveringUnattributedM2);
        }

        [Fact]
        public void The_Two_Totals_Never_Double_Count()
        {
            var d = ConsumableDrivers.From(
                new[] { Roof("IT4 Corrugated 0.5mm", 200), Roof("Generic - 225mm", 610.61) }, Table());

            Assert.Equal(200, d.RoofCoveringM2, 2);
            Assert.Equal(610.61, d.RoofCoveringUnattributedM2, 2);
        }

        [Fact]
        public void An_Unnameable_Roof_Measured_In_The_Wrong_Unit_Is_Not_Counted_Either()
        {
            // Same guard as every other driver: a quantity in one dimension is
            // never added to a total in another.
            var d = ConsumableDrivers.From(new[] { Roof("Generic - 225mm", 29, "each") }, Table());

            Assert.Equal(0, d.RoofCoveringUnattributedM2);
        }

        [Fact]
        public void A_Category_That_Is_Not_A_Roof_Is_Not_Unattributed_Roof()
        {
            var wall = new ConstituentInput
            {
                ConstituentKind = "", Category = "Walls",
                TypeName = "Nothing matches this", Quantity = 500, Unit = "m2"
            };

            var d = ConsumableDrivers.From(new[] { wall }, Table());

            Assert.Equal(0, d.RoofCoveringUnattributedM2);
        }

        // ── the sentence ────────────────────────────────────────────────────

        private static ConsumablesTally TallyWithAbsentRoofDriver(double unattributed)
        {
            var t = new ConsumablesTally();
            t.Consider("roof_fastener");
            t.RejectDriverAbsent("roof_fastener", "roof_covering_m2");
            t.RoofCoveringUnattributedM2 = unattributed;
            return t;
        }

        [Fact]
        public void A_Genuinely_Absent_Roof_Still_Reads_As_Intended_Behaviour()
        {
            string s = TallyWithAbsentRoofDriver(0).Summary();

            Assert.Contains("intended behaviour", s);
            Assert.DoesNotContain("WAS measured", s);
        }

        [Fact]
        public void An_Unattributed_Roof_Says_So_And_Says_What_To_Do()
        {
            string s = TallyWithAbsentRoofDriver(856.28).Summary();

            Assert.Contains("856 m²", s);
            Assert.Contains("WAS measured", s);
            Assert.Contains("product-specific", s);
            Assert.Contains("STING_SUPPLIER_UNITS.json", s);
        }

        [Fact]
        public void The_Extra_Sentence_Only_Appears_When_That_Driver_Is_The_Absent_One()
        {
            // An unattributed area with no absent roof rule is not a finding —
            // the rule may have fired from attributed area instead.
            var t = new ConsumablesTally();
            t.Consider("hoop_iron");
            t.RejectDriverAbsent("hoop_iron", "walled_area_m2");
            t.RoofCoveringUnattributedM2 = 856.28;

            Assert.DoesNotContain("WAS measured", t.Summary());
        }
    }
}
