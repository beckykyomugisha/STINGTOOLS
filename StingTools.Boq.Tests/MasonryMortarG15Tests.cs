using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// G-15 — one quantity, one owner.
    ///
    /// CST_S_MAS_MORTAR_VOLUME_CU_M queried BRICK_BOND unconditionally, so a blockwork
    /// wall (which carries no bond type) fell to BRICK_BOND DEFAULT = 0.025 m³/m² — a
    /// BRICK figure. BLOCK 400x200 MORTAR_VOLUME_FACTOR is 0.011. On 50 m² of 200mm
    /// blockwork the formula said 1.25 m³ and CompoundTakeoffBuilder said 0.55 m³:
    /// 2.27x apart, nothing flagged either side, and it propagated into the cement and
    /// sand quantities.
    ///
    /// The formula is deleted; CompoundTakeoff owns masonry mortar. These tests pin the
    /// surviving owner's arithmetic and the brick/block decision that selects its ratio.
    /// </summary>
    public class MasonryMortarG15Tests
    {
        private const double BlockMortarRatio = 0.011;   // MATERIAL_LOOKUP: BLOCK 400x200
        private const double BrickMortarRatio = 0.025;   // MATERIAL_LOOKUP: BRICK_BOND DEFAULT

        private static MasonryWallInput Wall(bool isBrick, double areaM2, double mortarRatio) =>
            new MasonryWallInput
            {
                FaceAreaM2 = areaM2,
                IsBrick = isBrick,
                UnitsPerM2 = isBrick ? 60 : 12.5,
                UnitWastePct = 5,
                PlasterFaces = 0,          // isolate mortar
                PlasterThicknessM = 0.013,
                PlasterWastePct = 20,
                MortarRatioM3PerM2 = mortarRatio,
                MortarCementBagsPerM3 = 9,
                MortarSandRatio = 1.25,
                PlasterCementBagsPerM3 = 9,
                PlasterSandRatio = 1.25,
                IsRcWall = false
            };

        private static double MortarM3(MasonryWallInput input) =>
            CompoundTakeoff.MasonryWall(input).Where(l => l.Kind == "mortar").Sum(l => l.Quantity);

        // ── the worked case from the finding ───────────────────────────────

        [Fact]
        public void Fifty_M2_Of_200mm_Blockwork_Yields_0_55_Cubic_Metres()
        {
            // 50 m² × 0.011 m³/m² = 0.55 m³ — the C# figure, and the correct one.
            Assert.Equal(0.55, MortarM3(Wall(isBrick: false, areaM2: 50.0, mortarRatio: BlockMortarRatio)), 3);
        }

        [Fact]
        public void The_Deleted_Formula_Would_Have_Said_1_25_Which_Is_2_27x_High()
        {
            // Pins the size of the defect: the formula applied the BRICK default to block.
            double wrong = 50.0 * BrickMortarRatio;
            Assert.Equal(1.25, wrong, 3);

            double right = MortarM3(Wall(isBrick: false, areaM2: 50.0, mortarRatio: BlockMortarRatio));
            Assert.Equal(2.27, wrong / right, 2);
        }

        [Fact]
        public void A_Brick_Wall_Yields_Its_Brick_Bond_Figure()
        {
            Assert.Equal(1.25, MortarM3(Wall(isBrick: true, areaM2: 50.0, mortarRatio: BrickMortarRatio)), 3);
        }

        // ── the brick/block decision (1.3) ─────────────────────────────────

        [Fact]
        public void Brick_Faced_Blockwork_Takes_The_Block_Branch()
        {
            // The defect that started this: a name containing "brick" on a block wall.
            Assert.False(MasonryClassifier.IsBrick("400x200", null, "Brick-faced blockwork"));
            // …and even with no dimensional evidence, "block" in the name vetoes "brick".
            Assert.False(MasonryClassifier.IsBrick(null, null, "Brick-faced blockwork"));
        }

        [Fact]
        public void Block_Evidence_Beats_A_Bond_Value()
        {
            // A block size is unambiguous; a stray bond value must not override it.
            Assert.False(MasonryClassifier.IsBrick("440x215", "STRETCHER", "Blockwork"));
        }

        [Fact]
        public void A_Brick_Wall_With_No_Bond_Parameter_Is_Still_Brick()
        {
            // Why "bond present ⇒ brick" was rejected as the test: InferBrickBond exists
            // because a real brick wall may carry no bond PARAMETER and resolve it from
            // the type name. Here the inferred value is what reaches the classifier.
            Assert.True(MasonryClassifier.IsBrick(null, "STRETCHER", "Clay brick"));
            // And with no evidence at all, the name still decides.
            Assert.True(MasonryClassifier.IsBrick(null, null, "Clay brick 225"));
        }

        [Fact]
        public void No_Evidence_And_No_Keyword_Is_Not_Brick()
        {
            // Defaulting to block is the safer failure: it is the lower ratio, so an
            // unclassifiable wall under-measures rather than inventing 2.27x the mortar.
            Assert.False(MasonryClassifier.IsBrick(null, null, "Generic masonry"));
            Assert.False(MasonryClassifier.IsBrick(null, null, null));
        }
    }
}
