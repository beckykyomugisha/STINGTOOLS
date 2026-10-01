using System.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-89 — suitability and revision are metadata, not part of a sheet's
    /// container id (ISO 19650; DTW-44 took them out of SheetNumberPolicy's ISO
    /// pattern). Nine types still ended their own pattern with -{suit}-{rev},
    /// and an ISO-shaped own pattern is used under both policies, so their
    /// numbers froze the status and revision the sheet had when it was made.
    /// </summary>
    public class SheetNumberStatusFreeTests
    {
        [Fact]
        public void No_shipped_sheet_number_pattern_carries_suitability_or_revision()
        {
            var types = DrawingCatalogueFixture.Shipped().DrawingTypes;
            var bad = types.Where(t => (t.SheetNumberPattern ?? "").Contains("{suit}")
                                    || (t.SheetNumberPattern ?? "").Contains("{rev}"))
                           .Select(t => $"{t.Id}: {t.SheetNumberPattern}").ToList();
            Assert.True(bad.Count == 0, "Sheet numbers that freeze status/revision:\n  " + string.Join("\n  ", bad));
            Assert.True(types.Count(t => (t.SheetNumberPattern ?? "").Contains("{project}")) >= 9,
                "the ISO-shaped patterns should still be ISO-shaped");
        }
    }
}
