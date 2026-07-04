using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-3 — compound wall/slab take-off emits measured CONSTITUENT line items
    /// (blockwork + plaster×faces + mortar (+ formwork for RC); concrete net +
    /// rebar + formwork) whose quantities match the ratios, instead of one
    /// composite m² rate. Tests the Document-free engine.
    /// </summary>
    public class CompoundTakeoffTests
    {
        private static MasonryWallInput BlockWall(int faces = 2) => new MasonryWallInput
        {
            FaceAreaM2 = 20.0,
            IsBrick = false,
            UnitsPerM2 = 12.5,
            UnitWastePct = 5,
            PlasterFaces = faces,
            PlasterThicknessM = 0.013,
            PlasterWastePct = 20,
            MortarRatioM3PerM2 = 0.011,
            MortarCementBagsPerM3 = 9,
            MortarSandRatio = 1.25,
            PlasterCementBagsPerM3 = 9,
            PlasterSandRatio = 1.25,
            IsRcWall = false
        };

        [Fact]
        public void Block_Wall_Emits_Blockwork_Plaster_And_Mortar()
        {
            var lines = CompoundTakeoff.MasonryWall(BlockWall());
            var kinds = lines.Select(l => l.Kind).ToList();
            Assert.Contains("blockwork", kinds);
            Assert.Contains("plaster", kinds);
            Assert.Contains("mortar", kinds);
            Assert.Contains("units", kinds);
            // No formwork for non-RC masonry.
            Assert.DoesNotContain("formwork", kinds);
        }

        [Fact]
        public void Blockwork_Area_Equals_Face_Area()
        {
            var line = CompoundTakeoff.MasonryWall(BlockWall()).Single(l => l.Kind == "blockwork");
            Assert.Equal("m2", line.Unit);
            Assert.Equal(20.0, line.Quantity, 3);
        }

        [Fact]
        public void Plaster_Area_Is_Face_Area_Times_Faces()
        {
            var oneFace = CompoundTakeoff.MasonryWall(BlockWall(1)).Single(l => l.Kind == "plaster");
            var twoFace = CompoundTakeoff.MasonryWall(BlockWall(2)).Single(l => l.Kind == "plaster");
            Assert.Equal(20.0, oneFace.Quantity, 3);
            Assert.Equal(40.0, twoFace.Quantity, 3);
        }

        [Fact]
        public void Mortar_Volume_And_Its_Sand_Match_The_Ratios()
        {
            var lines = CompoundTakeoff.MasonryWall(BlockWall());
            double mortar = lines.Single(l => l.Kind == "mortar").Quantity;
            double sand = lines.Single(l => l.Kind == "mortar_sand").Quantity;
            double cement = lines.Single(l => l.Kind == "mortar_cement").Quantity;
            Assert.Equal(20.0 * 0.011, mortar, 4);            // area × mortar ratio
            Assert.Equal(mortar * 1.25, sand, 4);             // MAT-2 sand ratio consumed
            Assert.Equal(mortar * 9, cement, 4);              // MAT-2 cement bags consumed
        }

        [Fact]
        public void Plaster_Cement_And_Sand_Derive_From_Plaster_Volume()
        {
            var lines = CompoundTakeoff.MasonryWall(BlockWall(2));
            double vol = 40.0 * 0.013 * 1.20;                 // area×faces × thk × (1+waste)
            double sand = lines.Single(l => l.Kind == "plaster_sand").Quantity;
            Assert.Equal(vol * 1.25, sand, 4);
        }

        [Fact]
        public void Rc_Wall_Adds_Formwork_Both_Faces()
        {
            var input = BlockWall();
            input.IsRcWall = true;
            var fw = CompoundTakeoff.MasonryWall(input).Single(l => l.Kind == "formwork");
            Assert.Equal(20.0 * 2, fw.Quantity, 3);           // both faces
            Assert.Equal("11", fw.Nrm2Section);               // formwork §11
        }

        [Fact]
        public void Constituent_Areas_Reconcile_To_The_Wall()
        {
            // The blockwork m² equals the wall face area (single source of the
            // composite area), and plaster is a multiple of it — a QS can check
            // the split reconciles against the modelled wall.
            var lines = CompoundTakeoff.MasonryWall(BlockWall(2));
            double block = lines.Single(l => l.Kind == "blockwork").Quantity;
            double plaster = lines.Single(l => l.Kind == "plaster").Quantity;
            Assert.Equal(block * 2, plaster, 3);
        }

        [Fact]
        public void Rc_Slab_Emits_Concrete_Net_Rebar_And_Formwork()
        {
            var lines = CompoundTakeoff.RcElement(new RcElementInput
            {
                ElementKind = "slab",
                ConcreteM3Net = 12.0,     // already net of voids
                RebarBandKgPerM3 = 80,
                FormworkM2 = 100
            });
            var concrete = lines.Single(l => l.Kind == "concrete");
            var rebar = lines.Single(l => l.Kind == "rebar");
            var formwork = lines.Single(l => l.Kind == "formwork");
            Assert.Equal(12.0, concrete.Quantity, 3);
            Assert.Equal(12.0 * 80, rebar.Quantity, 3);       // rebar = net concrete × band
            Assert.Equal(100, formwork.Quantity, 3);
            Assert.Equal("13", concrete.Nrm2Section);         // concrete §13
            Assert.Equal("15", rebar.Nrm2Section);            // reinforcement §15
        }

        [Fact]
        public void Zero_Area_Wall_Emits_Nothing()
        {
            var input = BlockWall();
            input.FaceAreaM2 = 0;
            Assert.Empty(CompoundTakeoff.MasonryWall(input));
        }
    }
}
