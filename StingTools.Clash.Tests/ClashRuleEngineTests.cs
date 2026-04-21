using System.Numerics;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashRuleEngineTests
    {
        [Fact]
        public void Tiny_Tessellation_Artifact_Is_Pseudo()
        {
            var engine = new ClashRuleEngine();
            var hit = new ClashHit { VolumeMm3 = 50f };
            var a = new ElementFacts { Category = "OST_Anything" };
            var b = new ElementFacts { Category = "OST_Anything" };

            var classified = engine.Classify(hit, a, b, null);
            Assert.Equal(ClashVerdict.Pseudo, classified.Verdict);
            Assert.Equal("R001_TESSELLATION_ARTIFACT", classified.VerdictRuleId);
        }

        [Fact]
        public void Duct_Insulation_Own_Duct_Is_Intentional()
        {
            var engine = new ClashRuleEngine();
            var hit = new ClashHit { VolumeMm3 = 100000f };   // big enough to pass R001
            var a = new ElementFacts { Category = "OST_DuctInsulations" };
            var b = new ElementFacts { Category = "OST_DuctCurves" };

            var classified = engine.Classify(hit, a, b, null);
            Assert.Equal(ClashVerdict.Intentional, classified.Verdict);
            Assert.Equal("R002_SELF_INSULATION", classified.VerdictRuleId);
        }

        [Fact]
        public void Normal_Pipe_Beam_Clash_Is_Kept()
        {
            var engine = new ClashRuleEngine();
            var hit = new ClashHit { VolumeMm3 = 10_000_000f, AabbMin = new Vector3(0, 0, 0), AabbMax = new Vector3(1, 1, 1) };
            var a = new ElementFacts { Category = "OST_PipeCurves" };
            var b = new ElementFacts { Category = "OST_StructuralFraming" };

            var classified = engine.Classify(hit, a, b, null);
            Assert.Equal(ClashVerdict.Keep, classified.Verdict);
            Assert.Null(classified.VerdictRuleId);
        }

        [Fact]
        public void Rec23_Column_Beam_Joint_Below_500L_Is_Intentional()
        {
            // rec-23: threshold bumped to 5e8 mm³ (500 L).
            var engine = new ClashRuleEngine();
            var hit = new ClashHit { VolumeMm3 = 4e8f };   // 400 L — below threshold
            var a = new ElementFacts { Category = "OST_StructuralColumns" };
            var b = new ElementFacts { Category = "OST_StructuralFraming" };
            var classified = engine.Classify(hit, a, b, null);
            Assert.Equal(ClashVerdict.Intentional, classified.Verdict);
            Assert.Equal("R008_STRUCTURAL_JOINT", classified.VerdictRuleId);
        }

        [Fact]
        public void Rec23_Column_Beam_Overlap_Above_500L_Is_Kept()
        {
            var engine = new ClashRuleEngine();
            var hit = new ClashHit { VolumeMm3 = 6e8f };   // 600 L — above threshold
            var a = new ElementFacts { Category = "OST_StructuralColumns" };
            var b = new ElementFacts { Category = "OST_StructuralFraming" };
            var classified = engine.Classify(hit, a, b, null);
            Assert.Equal(ClashVerdict.Keep, classified.Verdict);
        }
    }
}
