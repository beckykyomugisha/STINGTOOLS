using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the first real export ordered "164.48145 m³" of concrete and
    /// "350.3889 m²" of formwork timber, and one row read
    /// "0.479999210217142 m³". Order quantity is what somebody buys and what the
    /// amount is computed from, so raw binary floats leak into the money column
    /// as fractions of a shilling.
    ///
    /// Countable units round UP — you cannot buy 2.08 truck trips. Divisible
    /// units round UP to 2 dp, which is as precise as a delivery note gets.
    ///
    /// Both directions round UP because an order must never sit below the
    /// measured net. Half-up rounding was used here first, and the second real
    /// export caught it: 174.6044 m2 of blockwork ordered as 174.60.
    /// </summary>
    public class OrderQuantityRoundingTests
    {
        private static SupplierUnitRule Divisible() => new SupplierUnitRule
        {
            CommodityKey = "concrete-ready",
            SupplierUnit = "m³",
            SourceUnit = "m3",
            SourceUnitsPerSupplierUnit = 1.0,
            RoundUpToWhole = false,
            DefaultWastagePct = 5
        };

        private static SupplierUnitRule Countable() => new SupplierUnitRule
        {
            CommodityKey = "sand",
            SupplierUnit = "Trips (Sino Truck)",
            SourceUnit = "m3",
            SourceUnitsPerSupplierUnit = 12.0,
            RoundUpToWhole = true
        };

        [Fact]
        public void A_Divisible_Unit_Rounds_To_Two_Decimals()
        {
            // 156.6490... + 5% = 164.48145 → 164.49, NOT 164.48. Rounding to
            // nearest would put the order 0.00145 m3 under what was measured.
            var r = SupplierUnitConverter.Convert(Divisible(), 156.649);

            Assert.Equal(164.49, r.OrderQuantity, 6);
            Assert.True(r.OrderQuantity >= r.NetQuantity * 1.05 - 1e-9);
        }

        [Fact]
        public void Binary_Noise_Does_Not_Survive()
        {
            // The 0.479999210217142 case: a value that is 0.48 in every sense
            // that matters must not print sixteen digits of float residue.
            var rule = Divisible();
            rule.DefaultWastagePct = 0;

            var r = SupplierUnitConverter.Convert(rule, 0.479999210217142);

            Assert.Equal(0.48, r.OrderQuantity, 6);
        }

        [Fact]
        public void A_Countable_Unit_Still_Rounds_Up_Not_To_Two_Decimals()
        {
            // 25 m³ ÷ 12 = 2.083 trips → 3, never 2.08.
            var r = SupplierUnitConverter.Convert(Countable(), 25.0);

            Assert.Equal(3, r.OrderQuantity);
        }

        [Fact]
        public void A_Row_With_No_Rule_Is_Rounded_Too()
        {
            // The fallback path carried the measured quantity through untouched,
            // so "610.614588311426 m²" reached the sheet.
            var r = SupplierUnitConverter.Convert(null, 610.614588311426);

            Assert.Equal(610.62, r.OrderQuantity, 6);
            Assert.True(r.OrderQuantity >= r.NetQuantity);
        }

        [Fact]
        public void Net_Quantity_Keeps_Full_Precision()
        {
            // Net is the measured figure and stays exact — only the ORDER is a
            // rounded, purchasable number.
            var r = SupplierUnitConverter.Convert(Countable(), 25.0);

            Assert.Equal(25.0 / 12.0, r.NetQuantity, 9);
        }

        [Fact]
        public void Rounding_Never_Drops_Below_The_Net_Measured_Quantity()
        {
            // This test USED TO PASS while the bug was live, because it compared
            // the order against Math.Round(net, 2) — the very rounding under
            // test. A gate that checks a value against its own transformation
            // cannot fail. Compare against the RAW net, which is the contract.
            //
            // 174.6044 is the blockwork figure from the second real export, the
            // row that made rule R4 fire.
            var rule = Divisible();
            rule.DefaultWastagePct = 0;

            foreach (double q in new[] { 1.001, 2.004, 10.0049, 99.999, 174.6044, 610.614588311426 })
            {
                var r = SupplierUnitConverter.Convert(rule, q);
                Assert.True(r.OrderQuantity >= r.NetQuantity,
                    $"{q}: order {r.OrderQuantity} fell below net {r.NetQuantity}");
            }
        }

        [Fact]
        public void The_Amount_Derives_From_The_Rounded_Order()
        {
            var c = new MaterialCommodity { OrderQuantity = 164.48, RateUGX = 450000 };
            Assert.Equal(74016000, c.AmountUGX);   // not 74,016,652.5
        }
    }
}
