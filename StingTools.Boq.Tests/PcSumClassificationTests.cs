using System;
using System.Linq;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-44 — a Fohlio "pcSum" line is a prime cost sum, not a provisional sum,
    /// and every surface (bill, server sync, IFC) has to say which one it is.
    /// </summary>
    public class PcSumClassificationTests
    {
        [Theory]
        [InlineData("pcSum")]
        [InlineData("PC")]
        [InlineData("provisional")]   // the same Fohlio mechanism under its older name
        public void Fohlio_PcSum_Is_A_Prime_Cost_Sum(string treatment)
            => Assert.Equal(BOQRowSource.PCSum, FfeTreatment.RowSource(treatment));

        [Theory]
        [InlineData("ffe")]
        [InlineData("measured")]
        [InlineData("")]
        public void Other_Fohlio_Treatments_Stay_Model_Lines(string treatment)
            => Assert.Equal(BOQRowSource.Model, FfeTreatment.RowSource(treatment));

        [Fact]
        public void A_Fohlio_PcSum_Is_Never_Filed_As_A_Provisional_Sum()
            => Assert.NotEqual(BOQRowSource.ProvisionalSum, FfeTreatment.RowSource(FfeTreatment.PcSum));

        [Theory]
        [InlineData(BOQRowSource.Model, "Measured")]
        [InlineData(BOQRowSource.Manual, "Manual")]
        [InlineData(BOQRowSource.ProvisionalSum, "ProvisionalSum")]
        [InlineData(BOQRowSource.PCSum, "PcSum")]
        [InlineData(BOQRowSource.Dayworks, "Daywork")]
        public void Sync_LineKind_Per_Source(BOQRowSource src, string expected)
            => Assert.Equal(expected, BoqSourceUtil.SyncLineKind(src));

        [Fact]
        public void Only_Model_Lines_Sync_As_Measured()
        {
            // Enumerated, so a new source cannot silently go to the server as measured work.
            foreach (BOQRowSource s in Enum.GetValues(typeof(BOQRowSource)))
                Assert.Equal(s == BOQRowSource.Model, BoqSourceUtil.SyncLineKind(s) == "Measured");
        }

        [Fact]
        public void Every_Source_Has_Its_Own_Sync_LineKind()
        {
            var kinds = Enum.GetValues(typeof(BOQRowSource)).Cast<BOQRowSource>()
                .Select(BoqSourceUtil.SyncLineKind).ToList();
            Assert.Equal(kinds.Count, kinds.Distinct().Count());
            Assert.All(kinds, k => Assert.True(k.Length <= 20, $"{k} exceeds QuantityLine.LineKind max length 20"));
        }

        [Fact]
        public void Every_Label_Parses_Back_To_Its_Source()
        {
            foreach (BOQRowSource s in Enum.GetValues(typeof(BOQRowSource)))
                Assert.Equal(s, BoqSourceUtil.Parse(BoqSourceUtil.Label(s)));
        }

        [Fact]
        public void Bill_Prefix_Distinguishes_PC_From_PS()
        {
            string pc = BoqSourceUtil.BillPrefix(BOQRowSource.PCSum, ProvisionalSumType.Undeclared);
            Assert.Equal("PRIME COST SUM: ", pc);
            Assert.DoesNotContain("PROVISIONAL", pc);

            Assert.Equal("PROVISIONAL SUM (Defined): ",
                BoqSourceUtil.BillPrefix(BOQRowSource.ProvisionalSum, ProvisionalSumType.Defined));
            Assert.Equal("PROVISIONAL SUM (NOT DECLARED): ",
                BoqSourceUtil.BillPrefix(BOQRowSource.ProvisionalSum, ProvisionalSumType.Undeclared));
            Assert.Null(BoqSourceUtil.BillPrefix(BOQRowSource.Model, ProvisionalSumType.Defined));
        }

        [Theory]
        [InlineData(ProvisionalSumType.Defined, "Defined")]
        [InlineData(ProvisionalSumType.Undefined, "Undefined")]
        [InlineData(ProvisionalSumType.Undeclared, "NotDeclared")]
        public void Ps_Type_Wire_Token(ProvisionalSumType t, string expected)
        {
            string token = ProvisionalSumTypes.WireToken(t);
            Assert.Equal(expected, token);
            Assert.DoesNotContain(" ", token);
        }

        [Fact]
        public void Wire_Tokens_Read_Back_Except_NotDeclared()
        {
            Assert.True(ProvisionalSumTypes.TryParse(ProvisionalSumTypes.WireToken(ProvisionalSumType.Defined), out var d));
            Assert.Equal(ProvisionalSumType.Defined, d);
            Assert.True(ProvisionalSumTypes.TryParse(ProvisionalSumTypes.WireToken(ProvisionalSumType.Undefined), out var u));
            Assert.Equal(ProvisionalSumType.Undefined, u);
            Assert.False(ProvisionalSumTypes.TryParse(ProvisionalSumTypes.WireToken(ProvisionalSumType.Undeclared), out _));
        }
    }
}
