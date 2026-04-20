using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashMatrixTests
    {
        [Fact]
        public void Default_Has_At_Least_40_Cells_Post_Rec18()
        {
            var m = ClashMatrix.Default();
            Assert.True(m.Cells.Count >= 40,
                $"Expected ≥ 40 cells after rec-18; got {m.Cells.Count}");
        }

        [Fact]
        public void Match_Finds_Known_Pair()
        {
            var m = ClashMatrix.Default();
            var a = new ElementFacts { Category = "Ducts" };
            var b = new ElementFacts { Category = "Structural Framing" };
            // Filters target OST_* category names. Use those.
            var aOst = new ElementFacts { Category = "OST_DuctCurves" };
            var bOst = new ElementFacts { Category = "OST_StructuralFraming" };
            var cell = m.Match(aOst, bOst);
            Assert.NotNull(cell);
            Assert.Contains("DUCT", cell.PairId);
            Assert.Contains("STR_BEAM", cell.PairId);
        }

        [Fact]
        public void Match_Returns_Null_For_Unknown_Pair()
        {
            var m = ClashMatrix.Default();
            var a = new ElementFacts { Category = "OST_Rooms" };         // not in matrix
            var b = new ElementFacts { Category = "OST_Floors" };         // not paired with rooms
            Assert.Null(m.Match(a, b));
        }

        [Fact]
        public void Match_Is_Order_Independent()
        {
            var m = ClashMatrix.Default();
            var a = new ElementFacts { Category = "OST_PipeCurves" };
            var b = new ElementFacts { Category = "OST_Walls" };
            var c1 = m.Match(a, b);
            var c2 = m.Match(b, a);
            Assert.NotNull(c1);
            Assert.NotNull(c2);
            Assert.Equal(c1.PairId, c2.PairId);
        }

        [Fact]
        public void Disabled_Cell_Skipped()
        {
            var m = new ClashMatrix();
            m.Cells.Add(new ClashCell
            {
                PairId = "X:Y", FilterA = "Category=A", FilterB = "Category=B",
                Severity = "HIGH", Tolerance = "HARD", Enabled = false,
            });
            var a = new ElementFacts { Category = "A" };
            var b = new ElementFacts { Category = "B" };
            Assert.Null(m.Match(a, b));
        }

        [Fact]
        public void Wildcard_Filter_Matches_Prefix()
        {
            var m = new ClashMatrix();
            m.Cells.Add(new ClashCell
            {
                PairId = "W:P", FilterA = "Category=OST_Walls AND System=SA-*",
                FilterB = "Category=OST_PipeCurves",
                Severity = "MED", Tolerance = "HARD",
            });
            var a = new ElementFacts { Category = "OST_Walls", System = "SA-01" };
            var b = new ElementFacts { Category = "OST_PipeCurves" };
            Assert.NotNull(m.Match(a, b));
            a.System = "RA-01";
            Assert.Null(m.Match(a, b));
        }
    }
}
