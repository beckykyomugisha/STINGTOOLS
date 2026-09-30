// ViewTemplates phase 3: the temporary base views a template kind needs when the model has none.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TemporaryBaseViewsTests
    {
        [Theory]
        [InlineData(DrawingViewKind.Section)]
        [InlineData(DrawingViewKind.Rcp)]
        [InlineData(DrawingViewKind.ThreeD)]
        [InlineData(DrawingViewKind.Schedule)]
        [InlineData(DrawingViewKind.Drafting)]
        [InlineData(DrawingViewKind.FloorPlan)]
        public void A_missing_kind_gets_a_temporary_base_of_that_kind(string kind)
        {
            var steps = TemporaryBaseViews.Plan(kind, new List<string>(), out var why);
            Assert.Null(why);
            Assert.Equal(new[] { kind }, steps.ToArray());
        }

        [Fact]
        public void A_kind_the_model_has_needs_nothing()
        {
            Assert.Empty(TemporaryBaseViews.Plan(DrawingViewKind.Section, new[] { DrawingViewKind.Section }, out _));
        }

        [Fact]
        public void An_elevation_needs_a_plan_to_host_its_marker_first()
        {
            Assert.Equal(new[] { DrawingViewKind.FloorPlan, DrawingViewKind.Elevation },
                TemporaryBaseViews.Plan(DrawingViewKind.Elevation, new List<string>(), out _).ToArray());
            Assert.Equal(new[] { DrawingViewKind.Elevation },
                TemporaryBaseViews.Plan(DrawingViewKind.Elevation, new[] { DrawingViewKind.FloorPlan }, out _).ToArray());
        }

        [Fact]
        public void A_detail_template_is_made_from_a_section()
        {
            Assert.Equal(new[] { DrawingViewKind.Section },
                TemporaryBaseViews.Plan(DrawingViewKind.Detail, new List<string>(), out _).ToArray());
            Assert.Equal(DrawingViewKind.Section, TemporaryBaseViews.BaseKindFor(DrawingViewKind.Detail));
            Assert.Equal(DrawingViewKind.Rcp, TemporaryBaseViews.BaseKindFor(DrawingViewKind.Rcp));
        }

        [Fact]
        public void A_legend_cannot_be_made_and_says_why()
        {
            Assert.Null(TemporaryBaseViews.Plan(DrawingViewKind.Legend, new List<string>(), out var why));
            Assert.Contains("legend", why);
        }
    }
}
