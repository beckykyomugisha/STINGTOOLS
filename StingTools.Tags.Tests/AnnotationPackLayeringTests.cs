using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The production-rule and preset annotation overrides were written and never
    /// read, so the Production Config dialog's Annotation section did nothing. They
    /// now LAYER onto the drawing type's pack. These pin the layering so a later
    /// "simplification" to replace-wholesale — which would discard every type's
    /// authored rules in favour of the dialog's generic set — fails here.
    /// </summary>
    public class AnnotationPackLayeringTests
    {
        private static AutoAnnotationRule R(string type, string cat, bool on = true) =>
            new AutoAnnotationRule { RuleType = type, Category = cat, Enabled = on };

        private static AnnotationRulePack Base() => new AnnotationRulePack
        {
            Rules = new List<AutoAnnotationRule> { R("AutoTag", "Doors"), R("AutoTag", "Windows"), R("AutoDim", "Grids") },
            NorthArrowFamily = "BASE_NA",
        };

        [Fact]
        public void No_layer_returns_the_base_pack_itself()
        {
            var b = Base();
            Assert.Same(b, AnnotationPackLayering.Compose(b, null, null));
        }

        [Fact]
        public void Layer_overrides_the_rules_it_names_and_keeps_the_rest()
        {
            var over = new AnnotationRulePack { Rules = new List<AutoAnnotationRule> { R("AutoTag", "Windows", on: false), R("AutoTag", "Rooms") } };
            var p = AnnotationPackLayering.Compose(Base(), over);
            Assert.Equal(4, p.Rules.Count);
            Assert.False(p.Rules.Single(r => r.Category == "Windows").Enabled);
            Assert.Contains(p.Rules, r => r.Category == "Doors");
            Assert.Contains(p.Rules, r => r.Category == "Rooms");
            Assert.Equal("BASE_NA", p.NorthArrowFamily);   // silent scalar left alone
        }

        [Fact]
        public void Later_layer_wins_and_inputs_are_not_mutated()
        {
            var b = Base();
            var all = new AnnotationRulePack { NorthArrowFamily = "ALL" };
            var mine = new AnnotationRulePack { NorthArrowFamily = "MINE" };
            var p = AnnotationPackLayering.Compose(b, all, mine);
            Assert.Equal("MINE", p.NorthArrowFamily);
            Assert.Equal("BASE_NA", b.NorthArrowFamily);
            Assert.Equal(3, b.Rules.Count);
        }

        [Fact]
        public void Default_linear_strategy_on_a_layer_is_not_an_instruction()
        {
            var b = Base(); b.DimensionStrategy = "Chain";
            var p = AnnotationPackLayering.Compose(b, new AnnotationRulePack());
            Assert.Equal("Chain", p.DimensionStrategy);
        }
    }
}
