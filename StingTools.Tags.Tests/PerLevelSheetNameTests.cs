using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-72 — a type produced once per level has no section / elevation mark,
    /// so a {mark} in its sheet name printed XX ("PLANT ROOM — XX"). Per-level
    /// types name the level instead.
    /// </summary>
    public class PerLevelSheetNameTests
    {
        private static bool IsPerLevel(DrawingType t)
        {
            if (t.ProductionRules != null && t.ProductionRules.Count > 0)
                return t.ProductionRules.All(r =>
                {
                    var k = DrawingTemplateCatalogue.ViewKindOf(r.ViewType);
                    return k == DrawingViewKind.FloorPlan || k == DrawingViewKind.Rcp;
                });
            return DrawingPurposeViewKind.TryResolve(t.Purpose, out var kind)
                   && (kind == DrawingViewKind.FloorPlan || kind == DrawingViewKind.Rcp);
        }

        [Fact]
        public void Per_level_types_name_the_level_not_a_mark()
        {
            var perLevel = DrawingCatalogueFixture.Shipped().DrawingTypes.Where(IsPerLevel).ToList();
            Assert.True(perLevel.Count >= 40, $"Only {perLevel.Count} per-level types");
            var bad = perLevel.Where(t => (t.SheetNamePattern ?? "").Contains("{mark}"))
                              .Select(t => $"{t.Id}: '{t.SheetNamePattern}'").ToList();
            Assert.True(bad.Count == 0, "Per-level types printing {mark}:\n  " + string.Join("\n  ", bad));
        }
    }
}
