using System.Linq;
using StingTools.BOQ.Takeoff;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-4.2 — void-slab constituent split (precast/blocks separated from in-situ
    /// concrete) and MAT-4.3 — RC beam take-off.
    /// </summary>
    public class VoidSlabAndBeamTests
    {
        private static SlabCalcResult Maxspan() => SlabConcreteCalculator.Compute(new SlabCalcInput
        {
            ToppingMm = 65, RibWidthMm = 125, RibSpacingMm = 625, RibDepthMm = 200,
            PotWidthMm = 500, PotLengthMm = 600, RibsArePrecast = true
        });

        private static SlabCalcResult HollowPot() => SlabConcreteCalculator.Compute(new SlabCalcInput
        {
            ToppingMm = 50, RibWidthMm = 125, RibSpacingMm = 475, RibDepthMm = 225,
            PotWidthMm = 350, PotLengthMm = 300, RibsArePrecast = false
        });

        [Fact]
        public void Maxspan_Bills_Topping_Insitu_Plus_Separate_Precast_And_Blocks()
        {
            var lines = CompoundTakeoff.VoidSlab(Maxspan(), 100, "Maxspan", 120);
            var concrete = lines.Single(l => l.Kind == "concrete");
            var precast = lines.Single(l => l.Kind == "precast_rib");
            var blocks = lines.Single(l => l.Kind == "infill_block");

            // In-situ concrete = topping only (0.065 × 100 = 6.5 m³), NOT the solid
            // 0.265 × 100 = 26.5 m³ — precast is not mis-billed as in-situ.
            Assert.Equal(6.5, concrete.Quantity, 2);
            Assert.Equal("m3", concrete.Unit);
            // Precast ribs measured by length (m), excluded from the concrete.
            Assert.Equal("m", precast.Unit);
            Assert.True(precast.Quantity > 0);
            // Blocks measured nr.
            Assert.Equal("nr", blocks.Unit);
            Assert.True(blocks.Quantity > 0);
        }

        [Fact]
        public void Maxspan_Insitu_Concrete_Is_Far_Below_Solid_Measure()
        {
            var lines = CompoundTakeoff.VoidSlab(Maxspan(), 100, "Maxspan", 120);
            double insitu = lines.Single(l => l.Kind == "concrete").Quantity;
            double solid = (0.065 + 0.200) * 100; // gross solid measure
            Assert.True(insitu < solid / 3.0, $"in-situ {insitu} should be « solid {solid}");
        }

        [Fact]
        public void Void_Slab_Formwork_Is_Not_Gross_Soffit()
        {
            // Hollow-pot: pots are permanent formwork → props-only line, not m² soffit.
            var lines = CompoundTakeoff.VoidSlab(HollowPot(), 100, "Hollow-pot", 120);
            var fw = lines.Single(l => l.Kind == "formwork");
            Assert.NotEqual("m2", fw.Unit);            // not a gross-soffit m² line
            Assert.DoesNotContain(lines, l => l.Kind == "formwork" && l.Unit == "m2" && l.Quantity >= 100);
        }

        [Fact]
        public void Void_Slab_Rebar_Is_Rib_Bars_Plus_Mesh_Not_Flat_Band()
        {
            var lines = CompoundTakeoff.VoidSlab(HollowPot(), 100, "Hollow-pot", 120);
            var mesh = lines.Single(l => l.Kind == "mesh");
            var ribRebar = lines.Single(l => l.Kind == "rebar");
            Assert.Equal("m2", mesh.Unit);             // topping mesh by area
            // Rib rebar = in-situ rib CONCRETE × band, far below a flat 80 kg/m³ on
            // the gross slab volume (0.265 × 100 × 80 = 2120 kg).
            double flatBandGross = (0.050 + 0.225) * 100 * 80;
            Assert.True(ribRebar.Quantity < flatBandGross * 0.6,
                $"rib rebar {ribRebar.Quantity} should be « flat-band {flatBandGross}");
        }

        [Fact]
        public void Precast_System_Has_No_InSitu_Rib_Rebar()
        {
            // Maxspan ribs are precast (carry their own rebar) → no in-situ rib rebar.
            var lines = CompoundTakeoff.VoidSlab(Maxspan(), 100, "Maxspan", 120);
            Assert.DoesNotContain(lines, l => l.Kind == "rebar");
        }

        [Fact]
        public void Beam_Concrete_Formwork_And_Rebar()
        {
            // 300×600 beam, 5 m clear span, 150 slab bearing.
            var lines = CompoundTakeoff.RcBeam(new RcBeamInput
            {
                WidthM = 0.30, DepthM = 0.60, NetLengthM = 5.0, SlabBearingM = 0.150,
                RebarBandKgPerM3 = 120
            });
            var concrete = lines.Single(l => l.Kind == "concrete");
            var formwork = lines.Single(l => l.Kind == "formwork");
            var rebar = lines.Single(l => l.Kind == "rebar");
            Assert.Equal(0.30 * 0.60 * 5.0, concrete.Quantity, 4);          // 0.9 m³
            // formwork = (b + 2(D-ds))×L = (0.30 + 2·0.45)·5 = 6.0 m²
            Assert.Equal((0.30 + 2 * 0.45) * 5.0, formwork.Quantity, 4);
            Assert.Equal(concrete.Quantity * 120, rebar.Quantity, 3);
        }

        [Fact]
        public void Beam_Uses_Solid_Volume_Override_When_Provided()
        {
            // SolidVolume already deducts the column overlap.
            var lines = CompoundTakeoff.RcBeam(new RcBeamInput
            {
                WidthM = 0.30, DepthM = 0.60, NetLengthM = 5.0, SlabBearingM = 0.150,
                ConcreteM3Override = 0.82, RebarBandKgPerM3 = 120
            });
            Assert.Equal(0.82, lines.Single(l => l.Kind == "concrete").Quantity, 4);
        }
    }
}
