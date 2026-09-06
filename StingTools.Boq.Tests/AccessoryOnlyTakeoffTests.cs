using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// A decomposition can return rows and still have measured nothing about the
    /// host.
    ///
    /// This is the rule that keeps a roof in the schedule. A non-empty
    /// decomposition replaces the element's composite row; when the only row a
    /// roof produced was the fascia along its eaves, that replacement deleted the
    /// roof covering itself — 856 m² of it, from an issued export, with the two
    /// unpriced-item flags that had been reporting it disappearing at the same
    /// time, and the consumable driver it fed reading zero afterwards so the roof
    /// fastener rule printed a refusal instead of a gap.
    /// </summary>
    public class AccessoryOnlyTakeoffTests
    {
        [Fact]
        public void A_Fascia_Sits_On_The_Host_Without_Measuring_It()
        {
            Assert.True(CompoundTakeoff.IsAccessoryKind("fascia_board"));
        }

        [Theory]
        [InlineData("concrete")]
        [InlineData("rebar")]
        [InlineData("formwork")]
        [InlineData("tile")]
        [InlineData("screed")]
        [InlineData("dpm")]
        [InlineData("roof_underlay")]
        public void Everything_That_Measures_A_Face_Or_A_Volume_Is_Not_An_Accessory(string kind)
        {
            Assert.False(CompoundTakeoff.IsAccessoryKind(kind));
        }

        [Fact]
        public void An_Unknown_Kind_Measures_Its_Host_Until_Somebody_Says_Otherwise()
        {
            // The safe default. A wrong "measures" can double-count, which a
            // reader sees; a wrong "accessory" deletes a quantity, which nobody
            // sees.
            Assert.False(CompoundTakeoff.IsAccessoryKind("some_kind_added_next_year"));
        }

        [Fact]
        public void Accessories_Alone_Leave_The_Host_Unmeasured()
        {
            Assert.False(CompoundTakeoff.MeasuresHost(new[] { "fascia_board" }));
        }

        [Fact]
        public void One_Real_Constituent_Beside_An_Accessory_Measures_The_Host()
        {
            Assert.True(CompoundTakeoff.MeasuresHost(new[] { "fascia_board", "concrete" }));
        }

        [Fact]
        public void An_Empty_Decomposition_Measured_Nothing()
        {
            Assert.False(CompoundTakeoff.MeasuresHost(new string[0]));
            Assert.False(CompoundTakeoff.MeasuresHost(null));
        }

        [Fact]
        public void The_Roof_Accessory_Engine_Emits_Only_Accessory_Kinds()
        {
            // Ties the predicate to what the engine actually produces. If a
            // future row is added to RoofAccessories that DOES measure the roof,
            // this fails rather than letting the composite quietly disappear
            // again.
            var lines = CompoundTakeoff.RoofAccessories(new RoofEdgeInput
            {
                EavesLengthM = 12.5, RoofLabel = "Generic - 225mm"
            });

            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(CompoundTakeoff.IsAccessoryKind(l.Kind)));
            Assert.False(CompoundTakeoff.MeasuresHost(lines.Select(l => l.Kind)));
        }
    }
}
