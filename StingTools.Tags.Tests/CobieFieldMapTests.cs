using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Cobie;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// G4 — a COBie file imported into a model must survive being exported again.
    ///
    /// It did not. The import wrote ASS_INSTALLATION_DATE_TXT and the export read
    /// only COM_INSTALL_DATE_TXT, so the installation date fell through to a
    /// phase-derived fallback and came back out as a different value. Both halves
    /// looked correct in isolation; the two maps lived in two files and neither
    /// named the other, so nothing compared them.
    ///
    /// Extracting the map turned up a SECOND instance of the same defect that was
    /// still live: the import writes MNT_WARRANTY_START_TXT and the export read
    /// COM_WARRANTY_START_TXT, so an imported warranty start date did not survive
    /// either. Both are fixed; RoundTrip below is what stops a third.
    ///
    /// WHAT THIS CANNOT PROVE. It exercises the MAPPING, not the spreadsheet. It
    /// does not open Revit, so it cannot show that a real element accepted the
    /// write, that the worksheet parsed, or that element matching found the right
    /// element. The full round-trip through a model is a manual smoke-test step.
    /// </summary>
    public class CobieFieldMapTests
    {
        [Fact]
        public void EveryImportedColumnIsReadBackByTheExport()
        {
            // THE round-trip property. For every COBie column the import writes,
            // the export's read order must include the parameter it wrote --
            // otherwise the value is silently dropped on the way out.
            var dropped = new List<string>();
            foreach (var kv in CobieFieldMap.ComponentColumns)
            {
                var order = CobieFieldMap.ReadOrder(kv.Key);
                if (!order.Contains(kv.Value))
                    dropped.Add(kv.Key + " -> written to " + kv.Value + ", never read back");
            }

            Assert.True(dropped.Count == 0,
                "these COBie columns do not survive an import/export round-trip:\n  " +
                string.Join("\n  ", dropped));
        }

        [Theory]
        [InlineData("InstallationDate", "ASS_INSTALLATION_DATE_TXT")]
        [InlineData("WarrantyStartDate", "MNT_WARRANTY_START_TXT")]
        public void CanonicalParameterIsReadBeforeAnyLegacyAlias(string column, string canonical)
        {
            var order = CobieFieldMap.ReadOrder(column);

            Assert.NotEmpty(order);
            Assert.Equal(canonical, order[0]);

            // A model can carry both: one written by a recent import, one left
            // over from before the canonical parameter existed. Reading the alias
            // first would export the stale copy.
            Assert.True(order.Count > 1, column + " should still fall back to its legacy alias");
        }

        [Fact]
        public void LegacyAliasesAreReadOnlyAndAreNeverImportTargets()
        {
            // Writing to an alias on import would recreate the second copy this
            // consolidation exists to remove.
            var importTargets = new HashSet<string>(CobieFieldMap.ComponentColumns.Values);
            foreach (var kv in CobieFieldMap.LegacyReadFallbacks)
                foreach (var alias in kv.Value)
                    Assert.DoesNotContain(alias, importTargets);
        }

        [Fact]
        public void EveryLegacyFallbackNamesAColumnThatExists()
        {
            // A fallback keyed on a column the import does not write is dead
            // configuration: it would never be consulted, and it reads as
            // coverage that is not there.
            foreach (var column in CobieFieldMap.LegacyReadFallbacks.Keys)
                Assert.True(CobieFieldMap.ComponentColumns.ContainsKey(column),
                    "legacy fallback for '" + column + "' names no imported column");
        }

        [Fact]
        public void OneColumnMapsToOneParameterAndNoParameterIsSharedByTwoColumns()
        {
            // Two columns writing the same parameter means the second import
            // silently overwrites the first, and the export cannot tell which
            // column the surviving value came from.
            var byParam = CobieFieldMap.ComponentColumns
                .GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + " <- " + string.Join(", ", g.Select(x => x.Key)))
                .ToList();

            Assert.True(byParam.Count == 0,
                "these parameters are the target of more than one COBie column:\n  " +
                string.Join("\n  ", byParam));
        }

        [Theory]
        [InlineData("NotAColumn")]
        [InlineData("")]
        [InlineData(null)]
        public void AnUnknownColumnYieldsNoReadOrderRatherThanGuessing(string column)
        {
            // An empty list makes the caller's loop fall through to its own
            // fallback. Returning a guessed parameter name would put an
            // unrelated value in a COBie cell, which is worse than a blank.
            Assert.Empty(CobieFieldMap.ReadOrder(column));
        }
    }
}
