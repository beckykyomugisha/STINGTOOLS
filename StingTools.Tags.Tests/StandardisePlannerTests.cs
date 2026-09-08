using System.Collections.Generic;
using System.Linq;
using StingTools.Core;
using StingTools.Core.Baseline;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The two bulk operations the house standard needs, and the refusals that make
    /// them safe to run on a delivered model.
    ///
    /// <para>Both share one discipline: <b>they read what the model says, and stop where
    /// it says nothing.</b> A renamer that guessed would replace a name a reader can see
    /// is empty — "Generic - 225mm" — with one they cannot: "RC Slab 225" on a timber
    /// deck. A classifier that guessed would launder that guess into <c>MaterialClass</c>,
    /// the one controlled field the carbon and cost engines trust.</para>
    /// </summary>
    public class StandardisePlannerTests
    {
        private static MaterialLayer L(int i, string mat, double mm, bool structure = false)
            => new MaterialLayer { Index = i, MaterialName = mat, ThicknessMm = mm, IsStructure = structure };

        private static TypeRenameInput In(string cat, string name, params MaterialLayer[] ls)
            => new TypeRenameInput { Category = cat, CurrentName = name, Layers = ls.ToList(), InstanceCount = 5 };

        // ══════════════════════════════════════════════════════════════════════
        //  Renaming: the model supplies the words
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Wall_That_Could_Not_Be_Named_Gets_A_Name()
        {
            // Verbatim: 6 elements, a name carrying a colour and a thickness. Its
            // MATERIALS were right all along — which is the whole premise.
            var p = TypeRenamePlanner.Plan(In("Walls", "Exterior_CreamWhite_230 2",
                L(0, "Plaster - Cement Render 1:4", 12),
                L(1, "Masonry - Hollow Concrete Block 200mm", 200, structure: true),
                L(2, "Plaster - Cement Render 1:4", 12)));

            Assert.Equal("STING WL - Blockwork 200 - Plastered", p.ProposedName);
        }

        [Fact]
        public void The_Size_Comes_From_The_CORE_Not_The_Old_Name()
        {
            // "230" in the old name is the nominal wall; the block is 200. The proposal
            // must describe the thing, not repeat the number it was called.
            var p = TypeRenamePlanner.Plan(In("Walls", "Exterior_BrownWhite_230",
                L(0, "Plaster - Cement Render 1:4", 12),
                L(1, "Masonry - Hollow Concrete Block 200mm", 200, structure: true)));

            Assert.Contains("Blockwork 200", p.ProposedName);
            Assert.DoesNotContain("230", p.ProposedName);
        }

        [Fact]
        public void A_Tiled_Floor_Names_Its_Finish()
        {
            var p = TypeRenamePlanner.Plan(In("Floors", "stepsr 7",
                L(0, "Tile - Ceramic Floor 300x300", 10),
                L(1, "Screed - Cement Sand 1:3", 40),
                L(2, "Concrete C25", 150, structure: true)));

            Assert.Equal("STING FL - RC 150 - Ceramic Tiled", p.ProposedName);
        }

        [Fact]
        public void The_CORE_Wins_Over_A_Thicker_Non_Structural_Layer()
        {
            // Same rule as the PROD suffix reads. One definition, not two.
            var p = TypeRenamePlanner.Plan(In("Walls", "whatever",
                L(0, "Insulation - Mineral Wool", 200),
                L(1, "Timber - Softwood Cypress", 90, structure: true)));

            Assert.Contains("Timber 90", p.ProposedName);
        }

        // ── the refusals ──────────────────────────────────────────────────────

        [Fact]
        public void A_Type_Whose_Materials_Say_Nothing_Gets_NO_Proposal()
        {
            // The roof at the centre of this whole run: 11 elements, material
            // "Default Roof", nothing to read. Naming it would be inventing.
            var p = TypeRenamePlanner.Plan(In("Roofs", "Generic - 225mm",
                L(0, "Default Roof", 225, structure: true)));

            Assert.Null(p.ProposedName);
            Assert.Contains("names no substance", p.Reason);
            Assert.Contains("rename the MATERIAL first", p.Reason);
        }

        [Fact]
        public void A_Type_With_No_Materials_At_All_Gets_NO_Proposal()
        {
            var p = TypeRenamePlanner.Plan(In("Roofs", "Generic - 225mm", L(0, "", 225, structure: true)));
            Assert.Null(p.ProposedName);
            Assert.Contains("nothing to read", p.Reason);
        }

        [Fact]
        public void A_Conforming_Type_Is_Reported_But_Not_Renamed()
        {
            // Running twice must not churn. The second run proposes the same name and
            // recognises the type already has it.
            var input = In("Walls", "STING WL - Blockwork 200 - Plastered",
                L(0, "Plaster - Cement Render 1:4", 12),
                L(1, "Masonry - Hollow Concrete Block 200mm", 200, structure: true));

            var p = TypeRenamePlanner.Plan(input);
            Assert.True(p.IsProposal);
            Assert.True(p.AlreadyConforms);
        }

        [Fact]
        public void The_Summary_Says_The_Refusals_Are_Not_Failures()
        {
            var ps = TypeRenamePlanner.PlanAll(new[]
            {
                In("Walls", "a", L(0, "Masonry - Clay Brick 295x150x130", 295, structure: true)),
                In("Roofs", "b", L(0, "Default Roof", 225, structure: true)),
            });
            string s = TypeRenamePlanner.Summary(ps);
            Assert.Contains("1 can be renamed", s);
            Assert.Contains("1 cannot be named", s);
            Assert.Contains("not failures of this tool", s);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Material Class: fill the blank, never the chosen
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("Concrete C25", "Concrete")]
        [InlineData("Masonry - Hollow Concrete Block 200mm", "Masonry")]
        [InlineData("Masonry - Clay Brick 295x150x130", "Masonry")]
        [InlineData("Steel - Reinforcement Bar Y", "Metal")]
        [InlineData("Steel - Galvanised Sheet G28", "Metal")]
        [InlineData("Roofing - Stone Coated Tile", "Metal")]
        [InlineData("Timber - Hardwood Mvule", "Wood")]
        [InlineData("Tile - Porcelain Floor 600x600", "Ceramic")]
        [InlineData("Plaster - Cement Render 1:4", "Gypsum")]
        [InlineData("Bitumen - DPM 1000 Gauge", "Membrane")]
        [InlineData("Insulation - Mineral Wool", "Insulation")]
        [InlineData("Glass - Toughened 10mm", "Glass")]
        [InlineData("PVC - uPVC Section", "Plastic")]
        [InlineData("Hardcore - Stone Fill", "Stone")]
        [InlineData("Paint - Weatherguard Exterior", "Paint")]
        public void A_Named_Material_Is_Classified(string name, string expected)
        {
            Assert.Equal(expected, MaterialClassPlanner.Plan(name, "").ProposedClass);
        }

        [Fact]
        public void Stone_Coated_Steel_Tile_Is_METAL_Not_Stone()
        {
            // The ordering that makes the table work: the most specific needle first, or
            // "Stone Coated" reads as Stone and a steel roof tile is priced as masonry.
            Assert.Equal("Metal", MaterialClassPlanner.Plan("Roofing - Stone Coated Tile", "").ProposedClass);
            Assert.Equal("Stone", MaterialClassPlanner.Plan("Hardcore - Stone Fill", "").ProposedClass);
        }

        [Theory]
        [InlineData("Default Roof")]
        [InlineData("Material 12")]
        [InlineData("Finish - As Specified")]
        [InlineData("Exterior_CreamWhite_230")]
        public void A_Material_That_Names_No_Substance_Is_Left_BLANK(string name)
        {
            var p = MaterialClassPlanner.Plan(name, "");
            Assert.Null(p.ProposedClass);
            Assert.Contains("left blank rather than guessed", p.Reason);
        }

        [Theory]
        [InlineData("Concrete")]
        [InlineData("Wood")]
        public void An_EXISTING_Class_Is_Never_Overwritten(string existing)
        {
            // Somebody chose it. A bulk tool that replaces a human's classification is
            // worse than one that does nothing.
            var p = MaterialClassPlanner.Plan("Steel - Structural S275", existing);
            Assert.Null(p.ProposedClass);
            Assert.Contains("already classified", p.Reason);
        }

        [Fact]
        public void Unassigned_Counts_As_Blank()
        {
            // Revit's own placeholder, not a decision.
            Assert.Equal("Metal", MaterialClassPlanner.Plan("Steel - Structural S275", "Unassigned").ProposedClass);
        }

        [Fact]
        public void Every_Proposed_Class_Is_One_Revit_Actually_Uses()
        {
            // Inventing a class would fragment the very field this exists to make
            // dependable — the carbon and cost engines group on it.
            var known = new HashSet<string>(MaterialClassPlanner.RevitClasses);
            var names = new[]
            {
                "Concrete C25", "Masonry - Clay Brick", "Steel - Rebar", "Timber - Mvule",
                "Tile - Ceramic", "Plaster - Render", "Bitumen - Felt", "Insulation - EPS Board",
                "Glass - Float", "PVC - uPVC", "Hardcore - Stone", "Paint - Emulsion",
                "Sand - Building Sand", "Carpet - Wool",
            };
            foreach (string n in names)
            {
                string c = MaterialClassPlanner.Plan(n, "").ProposedClass;
                if (c != null) Assert.Contains(c, known);
            }
        }

        [Fact]
        public void The_Summary_Points_At_The_Materials_Worth_Renaming()
        {
            var ps = MaterialClassPlanner.PlanAll(new[]
            {
                ("Concrete C25", ""), ("Steel - Structural S275", "Metal"), ("Default Roof", ""),
            });
            string s = MaterialClassPlanner.Summary(ps);
            Assert.Contains("1 already classified and untouched", s);
            Assert.Contains("1 will be set", s);
            Assert.Contains("1 left blank", s);
            Assert.Contains("a blank Class is visible, and a guessed one is not", s);
        }
    }
}
