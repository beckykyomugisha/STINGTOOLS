using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ResolutionHeuristicsTests
    {
        [Fact]
        public void Returns_Null_For_Unpaired_Categories()
        {
            var c = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Unknown" },
                ElementB = new ClashElementRecord { Category = "Also Unknown" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            Assert.Null(ResolutionHeuristics.Suggest(c));
        }

        [Fact]
        public void Duct_Beam_Suggestion_Mentions_Lowering()
        {
            var c = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Ducts" },
                ElementB = new ClashElementRecord { Category = "Structural Framing" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            var s = ResolutionHeuristics.Suggest(c);
            Assert.NotNull(s);
            Assert.Contains("Lower duct", s);
            Assert.Contains("mm", s);
        }

        [Fact]
        public void Service_First_Normalisation_Handles_Swapped_Order()
        {
            var c = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Structural Framing" },
                ElementB = new ClashElementRecord { Category = "Ducts" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            var s = ResolutionHeuristics.Suggest(c);
            Assert.NotNull(s);
            Assert.Contains("duct", s.ToLower());
        }

        [Fact]
        public void Rec12_Sprinkler_Duct_Pattern_Returned()
        {
            // rec-12 new pattern.
            var c = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Sprinklers" },
                ElementB = new ClashElementRecord { Category = "Ducts" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 0.5f, 1f },
            };
            var s = ResolutionHeuristics.Suggest(c);
            Assert.NotNull(s);
            // Sprinkler-vs-Ducts pattern talks about "discharge cone" clearance.
            // Sprinkler-vs-CableTrays pattern uses the phrase "spray-pattern".
            // Either wording is acceptable — both reference BS EN 12845.
            Assert.True(s.Contains("discharge cone") || s.Contains("spray"),
                $"Expected sprinkler copy to mention discharge cone or spray; got: {s}");
        }

        [Fact]
        public void G4_Column_Floor_Pattern_Fires_In_Both_Orders()
        {
            // Both Structural Columns AND Floors are in IsStructural, so the
            // service-first swap doesn't run. Pre-G4 the rule only fired when
            // A happened to be the column; post-G4 MatchEither catches both.
            var c1 = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Structural Columns" },
                ElementB = new ClashElementRecord { Category = "Floors" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            var c2 = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Floors" },
                ElementB = new ClashElementRecord { Category = "Structural Columns" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            var s1 = ResolutionHeuristics.Suggest(c1);
            var s2 = ResolutionHeuristics.Suggest(c2);
            Assert.NotNull(s1);
            Assert.NotNull(s2);
            Assert.Equal(s1, s2);
            Assert.Contains("punching shear", s1);
        }

        [Fact]
        public void Rec12_Fire_Rating_Pattern_Returned()
        {
            // rec-12 new pattern: Pipes-Floors.
            var c = new ClashRecord
            {
                ElementA = new ClashElementRecord { Category = "Pipes" },
                ElementB = new ClashElementRecord { Category = "Floors" },
                AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
            };
            var s = ResolutionHeuristics.Suggest(c);
            Assert.NotNull(s);
            Assert.Contains("fire", s.ToLower());   // fire-rated sleeve / BS 9999
        }
    }
}
