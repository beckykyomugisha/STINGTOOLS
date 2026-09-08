using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// A wall is what holds it up, not what it is painted with.
    ///
    /// <para>Found on a real model. Every rendered exterior wall came back as
    /// gypsum:</para>
    /// <code>
    ///   48 x  CLAY BRICK VILLAGE LARGE (295x150x130MM)  ->  WL-MAS   correct
    ///   11 x  Exterior_CreamWhite_230 2                 ->  WL-GYP   wrong
    ///    5 x  Exterior_BrownWhite_230                   ->  WL-GYP   wrong
    /// </code>
    ///
    /// <para>The override table could not be at fault — <c>\bbrick|\bmasonry|\bblock</c>
    /// sits ABOVE <c>\bplaster|\bgypsum</c> in the file, so first-match-wins would
    /// have answered MAS had it seen the brick. It never saw the brick: the reader
    /// took <c>GetMaterialIds(false)</c> and returned the first non-zero id, which on
    /// a compound wall is whichever layer sorts first.</para>
    ///
    /// <para>The roofs showed the same shape from the other side: <c>Generic - 225mm</c>
    /// took no suffix while <c>Generic - 225mm 2</c> took <c>-BIT</c>. Two roofs of the
    /// same nominal build, told apart by layer order.</para>
    /// </summary>
    public class PrimaryMaterialSelectorTests
    {
        private static MaterialLayer L(int i, string mat, double mm, bool structure = false)
            => new MaterialLayer { Index = i, MaterialName = mat, ThicknessMm = mm, IsStructure = structure };

        // ══════════════════════════════════════════════════════════════════════
        //  The wall that produced the finding
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>A 230 mm rendered masonry wall, layered exterior-to-interior the
        /// way Revit lists it — render first, which is exactly how gypsum won.</summary>
        private static List<MaterialLayer> RenderedMasonryWall230() => new List<MaterialLayer>
        {
            L(0, "Plaster - Cream White",              12.5),
            L(1, "Masonry - Brick, Clay Village Large", 205, structure: true),
            L(2, "Plaster - Interior",                  12.5),
        };

        [Fact]
        public void The_230_Wall_Is_Masonry_Not_The_Render()
        {
            Assert.Equal("Masonry - Brick, Clay Village Large",
                         PrimaryMaterialSelector.Select(RenderedMasonryWall230()));
        }

        [Fact]
        public void First_Layer_Wins_Was_The_Bug_And_Is_Not_The_Rule()
        {
            // State the defect rather than describe it: the OLD behaviour, spelled
            // out, must not be what the selector produces.
            var layers = RenderedMasonryWall230();
            string oldAnswer = layers.First(l => !string.IsNullOrWhiteSpace(l.MaterialName)).MaterialName;

            Assert.Equal("Plaster - Cream White", oldAnswer);        // what it used to say
            Assert.NotEqual(oldAnswer, PrimaryMaterialSelector.Select(layers));
        }

        /// <summary>The single-layer wall that was ALREADY right must stay right —
        /// a fix that moves the 48 correct ones is not a fix.</summary>
        [Fact]
        public void A_Single_Layer_Masonry_Wall_Is_Unmoved()
        {
            Assert.Equal("Masonry - Brick",
                PrimaryMaterialSelector.Select(new[] { L(0, "Masonry - Brick", 295, structure: true) }));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The rule
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_THICKEST_Structural_Layer_Wins_Not_Merely_A_Structural_One()
        {
            // A cavity wall: two structural leaves, and the outer one is thinner.
            Assert.Equal("Blockwork 140", PrimaryMaterialSelector.Select(new[]
            {
                L(0, "Brick Facing 102", 102, structure: true),
                L(1, "Air",                50),
                L(2, "Blockwork 140",     140, structure: true),
            }));
        }

        [Fact]
        public void A_Thick_NON_Structural_Layer_Does_Not_Beat_A_Thinner_Structural_One()
        {
            // The insulation is thicker than the frame. The wall is still a timber
            // wall — this is why pass 1 is structure-only rather than plain thickest.
            Assert.Equal("Timber Stud", PrimaryMaterialSelector.Select(new[]
            {
                L(0, "Mineral Wool", 200),
                L(1, "Timber Stud",   90, structure: true),
            }));
        }

        [Fact]
        public void With_No_Structural_Layer_The_Thickest_Wins()
        {
            // The roofs: Generic - 225mm carries no declared structure at all.
            Assert.Equal("Concrete Deck", PrimaryMaterialSelector.Select(new[]
            {
                L(0, "Bitumen Membrane",   3),
                L(1, "Concrete Deck",    200),
                L(2, "Plaster Soffit",    12),
            }));
        }

        [Fact]
        public void A_Zero_Thickness_Membrane_Never_Names_The_Element()
        {
            // Exactly how -BIT came to name a roof: a membrane listed first.
            Assert.Equal("Concrete Deck", PrimaryMaterialSelector.Select(new[]
            {
                L(0, "Bitumen Membrane", 0),
                L(1, "Concrete Deck",  200),
            }));
        }

        [Fact]
        public void A_Zero_Width_STRUCTURAL_Layer_Does_Not_Beat_The_Real_Core()
        {
            // This is what makes the `ThicknessMm > 0` in the structural pass do work.
            // A barrier flagged Structure has no thickness to lose on, so without the
            // filter it wins pass 1 outright and the deck never gets considered.
            //
            // Written after a mutation showed the ORIGINAL zero-thickness filter (on
            // the non-structural pass) changed nothing at all — it was dead code, and
            // the test that "covered" it passed either way.
            Assert.Equal("Concrete Deck", PrimaryMaterialSelector.Select(new[]
            {
                L(0, "Vapour Barrier",   0, structure: true),
                L(1, "Concrete Deck",  200),
            }));
        }

        [Fact]
        public void But_A_Membrane_Answers_When_It_Is_All_There_Is()
        {
            // Returning null here would send the caller to the fallback, which would
            // pick the same layer anyway — with an extra Revit read and no reason.
            Assert.Equal("Bitumen Membrane",
                PrimaryMaterialSelector.Select(new[] { L(0, "Bitumen Membrane", 0) }));
        }

        [Fact]
        public void Equal_Thickness_Breaks_On_ORDER_So_The_Answer_Is_Stable()
        {
            // Without a tie-break the answer depends on the order a collector
            // happened to return, which is how this whole defect started.
            var layers = new[]
            {
                L(0, "Blockwork A", 100, structure: true),
                L(1, "Blockwork B", 100, structure: true),
            };
            Assert.Equal("Blockwork A", PrimaryMaterialSelector.Select(layers));
            Assert.Equal("Blockwork A", PrimaryMaterialSelector.Select(layers.Reverse().ToArray()));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  What it refuses to answer
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void No_Material_Means_NULL_So_The_Caller_Falls_Back(int count)
        {
            var layers = Enumerable.Range(0, count).Select(i => L(i, null, 100)).ToArray();
            Assert.Null(PrimaryMaterialSelector.Select(layers));
        }

        [Fact]
        public void Null_And_Empty_Are_Null_Not_An_Exception()
        {
            Assert.Null(PrimaryMaterialSelector.Select(null));
            Assert.Null(PrimaryMaterialSelector.Select(new MaterialLayer[0]));
            Assert.Null(PrimaryMaterialSelector.Select(new MaterialLayer[] { null }));
            Assert.Null(PrimaryMaterialSelector.Select(new[] { L(0, "   ", 100) }));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  End to end, through the SHIPPED override table
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The selector is only worth having if the suffix it produces is different.
        /// This runs the chosen material through the real rules to show WL-GYP
        /// becoming WL-MAS — the whole point, asserted rather than asserted about.
        /// </summary>
        [Fact]
        public void The_Rendered_Wall_Now_Takes_MAS_Where_It_Took_GYP()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");

            var rules = MaterialProdOverrideRules.Parse(File.ReadAllLines(
                Path.Combine(dir.FullName, "StingTools", "Data", "STING_MATERIAL_PROD_OVERRIDES.csv")));

            var layers = RenderedMasonryWall230();

            string oldMat = layers.First().MaterialName;                 // first-layer-wins
            string newMat = PrimaryMaterialSelector.Select(layers);      // core-wins

            Assert.Equal("GYP", MaterialProdOverrideRules.ResolveSuffix(rules, oldMat, "Walls"));
            Assert.Equal("MAS", MaterialProdOverrideRules.ResolveSuffix(rules, newMat, "Walls"));
        }
    }
}
