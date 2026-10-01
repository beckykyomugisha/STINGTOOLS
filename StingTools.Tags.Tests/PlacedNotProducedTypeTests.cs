using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-66 — legend-A3 is routed (* * LEGEND) but its only view is a legend,
    /// which the Revit API cannot create. It is a place-existing type: it names
    /// no view template (a legend takes none) and says how it is used.
    /// </summary>
    public class PlacedNotProducedTypeTests
    {
        [Fact]
        public void Shipped_view_templates_are_all_creatable()
        {
            // "STING - Multi-Discipline Legend" was reported as not creatable on
            // every View Templates run.
            var plan = DrawingTemplateCatalogue.Plan(DrawingCatalogueFixture.Shipped().DrawingTypes);
            Assert.True(plan.NotCreatable.Count == 0, "Not creatable:\n  " + string.Join("\n  ", plan.NotCreatable));
        }

        [Fact]
        public void A_type_that_cannot_be_produced_says_it_is_placed()
        {
            var placed = DrawingCatalogueFixture.Shipped().DrawingTypes
                .Where(t => (t.ProductionRules == null || t.ProductionRules.Count == 0)
                            && DrawingPurposeViewKind.TryResolve(t.Purpose, out var k)
                            && !DrawingPurposeViewKind.IsProducible(k))
                .ToList();
            Assert.Contains(placed, t => t.Id == "legend-A3");
            Assert.All(placed, t =>
            {
                Assert.True(string.IsNullOrWhiteSpace(t.ViewTemplateName), $"{t.Id} names a template its view cannot take");
                Assert.Contains("Placed, not produced", t.Description ?? "");
            });
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeValidator.cs");
            Assert.Contains("\"DT-032\"", src);
        }
    }
}
