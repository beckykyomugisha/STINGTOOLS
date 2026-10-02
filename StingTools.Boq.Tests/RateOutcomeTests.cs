using System.Collections.Generic;
using System.Linq;
using StingTools.BOQ.Rates;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-26. A deliberate zero could not be expressed: <c>UnitRate &lt;= 0</c> meant
    /// "no rate" everywhere, so an item priced at nil fell through to the next
    /// provider. A zero now has to be DECLARED (NIL / INCL / INCL:&lt;ref&gt;), a declared
    /// outcome stops the chain, and a bare 0 is reported and treated as not priced.
    /// </summary>
    public class RateOutcomeTests
    {
        private const string Header = "Category,PROD,MAT_CODE,MAT_DISCIPLINE,Unit_Rate_USD,Unit_Rate_UGX,Unit,Description";

        private static CostRateCsv.Result Parse(params string[] rows) =>
            CostRateCsv.Parse(new[] { Header }.Concat(rows), CommodityRateResolver.SplitCsvLine);

        [Theory]
        [InlineData("NIL", RateOutcome.Nil, "")]
        [InlineData(" nil ", RateOutcome.Nil, "")]
        [InlineData("INCL", RateOutcome.Included, "")]
        [InlineData("Incl.", RateOutcome.Included, "")]
        [InlineData("INCLUDED", RateOutcome.Included, "")]
        [InlineData("INCL:E10/2", RateOutcome.Included, "E10/2")]
        [InlineData("included: item 14.3.2", RateOutcome.Included, "item 14.3.2")]
        public void Declarations_Parse(string cell, RateOutcome expected, string expectedRef)
        {
            Assert.True(RateOutcomeToken.TryParse(cell, out var o, out string r));
            Assert.Equal(expected, o);
            Assert.Equal(expectedRef, r);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("0.00")]
        [InlineData("TBC")]
        [InlineData("INCLUSIVE")]
        [InlineData("NIL:x")]
        [InlineData("N/A")]
        public void Anything_Else_Is_Not_A_Declaration(string cell)
        {
            Assert.False(RateOutcomeToken.TryParse(cell, out var o, out _));
            Assert.Equal(RateOutcome.Priced, o);
        }

        [Theory]
        [InlineData(RateOutcome.Nil, "")]
        [InlineData(RateOutcome.Included, "")]
        [InlineData(RateOutcome.Included, "E10/2")]
        public void Tokens_Round_Trip(RateOutcome o, string reference)
        {
            string token = RateOutcomeToken.ToToken(o, reference);
            Assert.True(RateOutcomeToken.TryParse(token, out var back, out string r));
            Assert.Equal(o, back);
            Assert.Equal(reference, r);
        }

        [Fact]
        public void Bill_Text_Is_What_A_QS_Writes()
        {
            Assert.Equal("Nil", RateOutcomeToken.BillRateText(RateOutcome.Nil, ""));
            Assert.Equal("Incl.", RateOutcomeToken.BillRateText(RateOutcome.Included, ""));
            Assert.Equal("Incl. in E10/2", RateOutcomeToken.BillRateText(RateOutcome.Included, "E10/2"));
            Assert.Null(RateOutcomeToken.BillRateText(RateOutcome.Priced, ""));
            Assert.Equal("NIL", RateOutcomeToken.StampText(RateOutcome.Nil, "", 0));
            Assert.Equal("315000", RateOutcomeToken.StampText(RateOutcome.Priced, "", 315000));
        }

        [Fact]
        public void Chain_Stops_On_A_Declaration_And_Moves_On_From_A_Bare_Zero()
        {
            Assert.Equal(RateChainStep.Continue, RateChainRule.Decide(false, RateOutcome.Priced, 0));
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, RateOutcome.Priced, 10));
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, RateOutcome.Nil, 0));
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, RateOutcome.Included, 0));
            Assert.Equal(RateChainStep.ContinueUndeclaredZero, RateChainRule.Decide(true, RateOutcome.Priced, 0));
            Assert.True(RateChainRule.IsDecided(RateOutcome.Nil, 0));
            Assert.False(RateChainRule.IsDecided(RateOutcome.Priced, 0));
        }

        [Fact]
        public void A_Nil_Row_Parses_Cleanly_And_Claims_Its_Keys()
        {
            var res = Parse("Rooms,RM,RM,A,NIL,NIL,each,Room (no direct cost)");
            Assert.Empty(res.Problems);
            var row = Assert.Single(res.Rows);
            Assert.Equal(RateOutcome.Nil, row.Outcome);

            var rates = CostRateCsv.ToTables(res, out var declared);
            Assert.Empty(rates);
            Assert.Equal(RateOutcome.Nil, declared["Rooms"].Outcome);
            Assert.Equal(RateOutcome.Nil, declared["A|RM"].Outcome);
            Assert.Equal(RateOutcome.Nil, declared["RM"].Outcome);
        }

        [Fact]
        public void An_Included_Row_Keeps_Its_Reference()
        {
            var res = Parse("Pipe Insulation,PIN,PIN,M,INCL:Pipes,INCL:Pipes,m,Included with pipework");
            Assert.Empty(res.Problems);
            CostRateCsv.ToTables(res, out var declared);
            Assert.Equal(RateOutcome.Included, declared["Pipe Insulation"].Outcome);
            Assert.Equal("Pipes", declared["Pipe Insulation"].IncludedIn);
        }

        [Fact]
        public void A_Bare_Zero_Is_Reported_And_Claims_No_Key()
        {
            var res = Parse("Walls,WL,WAL,A,0.00,0,m2,zero",
                            "Walls,WL2,WAL2,A,85.00,315000,m2,Standard wall");
            Assert.Contains(res.Problems, p => p.Contains("zero rate without NIL / INCL"));
            Assert.True(res.Rows[0].IsUndeclaredZero);

            var dupes = new List<string>();
            var rates = CostRateCsv.ToTables(res, out var declared, dupes);
            // The 0 row did not take the key, so the priced row below it owns it.
            Assert.Equal(315000, rates["Walls"].rate);
            Assert.Empty(declared);
            Assert.DoesNotContain("Walls", dupes);
        }

        [Fact]
        public void A_Declaration_Beside_A_Number_Is_Refused()
        {
            var res = Parse("Walls,WL,WAL,A,85.00,NIL,m2,contradictory");
            Assert.Empty(res.Rows);
            Assert.Contains(res.Problems, p => p.Contains("contradictory"));
        }

        [Fact]
        public void First_Row_Owns_A_Key_Across_Both_Tables()
        {
            var res = Parse("Rooms,RM,RM,A,NIL,NIL,each,nil first",
                            "Rooms,RM2,RM2,A,10,37000,each,priced second");
            var dupes = new List<string>();
            var rates = CostRateCsv.ToTables(res, out var declared, dupes);
            Assert.Equal(RateOutcome.Nil, declared["Rooms"].Outcome);
            Assert.False(rates.ContainsKey("Rooms"));
            Assert.Contains("Rooms", dupes);
        }

        [Fact]
        public void An_Undeclared_Zero_On_The_Product_Row_Falls_Through_To_The_Category()
        {
            // Before DSCH-26 the PROD pass returned the 0, and the registry then threw
            // away the whole CSV answer - the category rate in the same file included.
            var rates = new Dictionary<string, (double rate, string unit)>
            {
                ["M|AHU"] = (0, "each"),
                ["Mechanical Equipment"] = (31450000, "each"),
            };
            var m = CsvRateLookup.Resolve(rates, "t.csv", "Mechanical Equipment", "M", "AHU", "", "");
            Assert.NotNull(m);
            Assert.Equal(31450000, m.UnitRate);
            Assert.Equal(RateResolutionLevel.Category, m.Level);
            Assert.Equal(RateOutcome.Priced, m.Outcome);
        }

        [Fact]
        public void A_Declared_Nil_On_The_Product_Row_Stops_The_Passes()
        {
            var rates = new Dictionary<string, (double rate, string unit)>
            {
                ["Mechanical Equipment"] = (31450000, "each"),
            };
            var declared = new Dictionary<string, DeclaredRate>
            {
                ["M|AHU"] = new DeclaredRate { Outcome = RateOutcome.Nil, Unit = "each" },
            };
            var m = CsvRateLookup.Resolve(rates, "t.csv", "Mechanical Equipment", "M", "AHU", "", "", declared);
            Assert.NotNull(m);
            Assert.Equal(RateOutcome.Nil, m.Outcome);
            Assert.Equal(0, m.UnitRate);
            Assert.Equal("M|AHU", m.MatchedKey);
            Assert.Contains("declared NIL", m.Provenance);
        }

        // The shipped card's Rooms NIL row was removed: Rooms are NOT MEASURED in
        // STING_DEFAULT_COST_RATES.csv and never reach pricing, so a rate (even a
        // declared nil) for them would be a second owner of the same decision.
        // DefaultCostRatesCsvTests.Shipped_Card_Has_No_Row_For_A_Not_Measured_Category
        // holds the two files together.
    }
}
