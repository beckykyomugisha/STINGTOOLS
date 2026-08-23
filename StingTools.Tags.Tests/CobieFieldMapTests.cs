using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Cobie;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// G4 — a COBie file imported into a model must survive being exported again,
    /// and every column must name a parameter that can actually receive a value.
    ///
    /// TWO DISTINCT DEFECTS, both silent.
    ///
    /// 1. The two halves disagreed. The import wrote ASS_INSTALLATION_DATE_TXT and
    ///    the export read only COM_INSTALL_DATE_TXT, so the installation date fell
    ///    through to a phase-derived fallback and came back out as a different
    ///    value. Three hand-written copies of the map lived in three files and
    ///    none named the others, so nothing compared them.
    ///
    /// 2. Eleven targets across those maps named parameters that DO NOT EXIST in
    ///    PARAMETER_REGISTRY.json — the three MNT_WARRANTY_* names, ASS_MODEL_NUM_TXT,
    ///    MNT_EXPECTED_LIFE_TXT, ASS_REPLACEMENT_COST_TXT, BLE_LENGTH/WIDTH/HEIGHT_TXT
    ///    and ASS_COLOUR_TXT. ParameterHelpers.SetString returns false when the
    ///    parameter is not on the element, so those columns were read from the
    ///    spreadsheet and discarded without a word. Warranty guarantor and warranty
    ///    duration are required at rung 500 by the KUT LOD overlay, so a COBie
    ///    handover file could not satisfy the close-out gate by import alone.
    ///
    /// The first defect is caught by comparing the halves. The second is not, and
    /// cannot be: a map validated only against itself certifies its own mistakes.
    /// That is the blind spot tools/build_kut_lod_overlay.py already closed with a
    /// binding gate, and the assertions in the second half of this file are the
    /// same idea applied to the same class of data.
    ///
    /// WHAT THIS CANNOT PROVE. It exercises the MAPPING, not the spreadsheet. It
    /// does not open Revit, so it cannot show that a real element accepted the
    /// write, that the worksheet parsed, or that element matching found the right
    /// element. The full round-trip through a model is smoke-test step 34.
    /// </summary>
    public class CobieFieldMapTests
    {
        // -- the two halves against each other ----------------------------------

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
                "these COBie columns do not survive an import/export round-trip:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", dropped));
        }

        [Fact]
        public void EveryTypeColumnWithAReadOrderIsReadBackByTheExport()
        {
            var dropped = new List<string>();
            foreach (var kv in CobieFieldMap.TypeColumns)
            {
                var order = CobieFieldMap.TypeReadOrder(kv.Key);
                if (order.Count > 0 && !order.Contains(kv.Value))
                    dropped.Add(kv.Key + " -> written to " + kv.Value + ", never read back");
            }

            Assert.True(dropped.Count == 0,
                "these COBie Type columns do not survive a round-trip:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", dropped));
        }

        [Theory]
        [InlineData("InstallationDate", "ASS_INSTALLATION_DATE_TXT")]
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
        public void TheTypeModelNumberFallsBackToTheParameterTheExportUsedToRead()
        {
            // The export read ASS_MODEL_NR_TXT while the close-out gate reads
            // ASS_MODEL_REF_TXT. Pointing the import at the gate's parameter
            // without keeping this fallback would have broken the round-trip in
            // the opposite direction -- the same defect, newly introduced.
            var order = CobieFieldMap.TypeReadOrder("ModelNumber");
            Assert.Equal("ASS_MODEL_REF_TXT", order[0]);
            Assert.Contains("ASS_MODEL_NR_TXT", order);
        }

        [Fact]
        public void LegacyAliasesAreReadOnlyAndAreNeverImportTargets()
        {
            // Writing to an alias on import would recreate the second copy this
            // consolidation exists to remove.
            var importTargets = new HashSet<string>(
                CobieFieldMap.ComponentColumns.Values.Concat(CobieFieldMap.TypeColumns.Values),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kv in CobieFieldMap.LegacyReadFallbacks)
                foreach (var alias in kv.Value)
                    Assert.DoesNotContain(alias, importTargets);

            foreach (var kv in CobieFieldMap.TypeLegacyReadFallbacks)
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
                    "legacy fallback for '" + column + "' names no imported Component column");

            foreach (var column in CobieFieldMap.TypeLegacyReadFallbacks.Keys)
                Assert.True(CobieFieldMap.TypeColumns.ContainsKey(column),
                    "legacy fallback for '" + column + "' names no imported Type column");
        }

        [Fact]
        public void OneColumnMapsToOneParameterWithinAWorksheet()
        {
            // Two columns writing the same parameter means the second import
            // silently overwrites the first, and the export cannot tell which
            // column the surviving value came from. Checked per worksheet: the
            // Component and Type sheets legitimately share names such as
            // Description, because they write to different elements.
            foreach (var sheet in new[]
                     {
                         new { Name = "Component", Map = CobieFieldMap.ComponentColumns },
                         new { Name = "Type", Map = CobieFieldMap.TypeColumns },
                     })
            {
                var shared = sheet.Map
                    .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key + " <- " + string.Join(", ", g.Select(x => x.Key)))
                    .ToList();

                Assert.True(shared.Count == 0,
                    "on the " + sheet.Name + " sheet these parameters are the target of more " +
                    "than one column:" + Environment.NewLine + "  " +
                    string.Join(Environment.NewLine + "  ", shared));
            }
        }

        // -- the map against the shipped data, not against itself ---------------

        [Fact]
        public void EveryTargetExistsInTheParameterRegistry()
        {
            var missing = CobieFieldMap.AllTargets
                .Where(p => !CobieBindingFacts.Registered.Contains(p))
                .OrderBy(p => p)
                .ToList();

            Assert.True(missing.Count == 0,
                "these COBie targets are not in PARAMETER_REGISTRY.json, so SetString writes " +
                "nothing and the column is silently discarded:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", missing));
        }

        [Fact]
        public void EveryTargetResolvesInResolvedBindings()
        {
            var unbound = CobieFieldMap.AllTargets
                .Where(p => !CobieBindingFacts.Bindings.ContainsKey(p))
                .OrderBy(p => p)
                .ToList();

            Assert.True(unbound.Count == 0,
                "these COBie targets are not bound to any category, so they cannot be filled " +
                "and requiring them is indistinguishable from not requiring them:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", unbound));
        }

        [Fact]
        public void EveryTargetIsTextTypedBecauseSetStringRefusesAnythingElse()
        {
            // ParameterHelpers.SetString returns false when StorageType != String,
            // so a numeric target fails exactly as silently as a missing one. The
            // _YRS and _UGX suffixes are naming convention, not storage type,
            // which is why this is read from the shared-parameter file rather
            // than inferred from the name.
            var wrong = CobieFieldMap.AllTargets
                .Select(p => new
                {
                    Param = p,
                    Type = CobieBindingFacts.Datatypes.ContainsKey(p)
                        ? CobieBindingFacts.Datatypes[p]
                        : "(absent from MR_PARAMETERS.txt)"
                })
                .Where(x => x.Type != "TEXT")
                .OrderBy(x => x.Param)
                .ToList();

            Assert.True(wrong.Count == 0,
                "these COBie targets are not TEXT, so SetString refuses them:" +
                Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ",
                    wrong.Select(x => x.Param + " is " + x.Type)));
        }

        [Fact]
        public void ATargetBoundToOnlySomeCategoriesMustBeDeclaredAsSuch()
        {
            // A narrowly-bound target still discards data, just on the categories
            // it does not reach: COM_WARRANTY_START_TXT reaches five comms and
            // security categories, so a warranty start date imported onto a
            // chiller goes nowhere. That is tolerable only while it is VISIBLE,
            // so anything not universally bound must be declared in NarrowlyBound
            // with the categories it actually reaches.
            var undeclared = CobieFieldMap.AllTargets
                .Where(p => !CobieBindingFacts.IsUniversal(p))
                .Where(p => !CobieFieldMap.NarrowlyBound.ContainsKey(p))
                .OrderBy(p => p)
                .ToList();

            Assert.True(undeclared.Count == 0,
                "these COBie targets are bound to only some categories but are not declared " +
                "in CobieFieldMap.NarrowlyBound, so the silent-discard hazard is invisible:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", undeclared));
        }

        [Fact]
        public void TheDeclaredNarrowBindingsMatchTheShippedBindings()
        {
            // The declaration is documentation, and documentation drifts. Pin it
            // to the file so a binding change either updates the declaration or
            // fails here.
            foreach (var kv in CobieFieldMap.NarrowlyBound)
            {
                Assert.True(CobieBindingFacts.Bindings.ContainsKey(kv.Key),
                    kv.Key + " is declared narrowly bound but is not in RESOLVED_BINDINGS.csv");

                var actual = CobieBindingFacts.Bindings[kv.Key].OrderBy(x => x).ToArray();
                var declared = kv.Value.OrderBy(x => x).ToArray();
                Assert.True(actual.SequenceEqual(declared),
                    kv.Key + " reaches " + string.Join(", ", actual) +
                    " but CobieFieldMap.NarrowlyBound declares " + string.Join(", ", declared));
            }
        }

        [Fact]
        public void TheCloseOutGateParametersAreImportable()
        {
            // The point of the whole exercise. The KUT LOD overlay requires these
            // at rung 500 for Tier A and Tier C. While no COBie column wrote them,
            // a handover file could not satisfy the close-out gate by import
            // alone -- the gate correctly reported data missing that the importer
            // had read and thrown away.
            // Asserted against the COMPONENT sheet specifically. LOD verification
            // reads these off the element instance, so importing them onto the
            // ElementType via the Type sheet would not satisfy it. An earlier
            // version of this test accepted either sheet and passed while the
            // Component map was broken -- the assertion has to name the path the
            // gate actually reads.
            var required = new[] { "ASS_WARRANTY_PARTS_TXT", "ASS_WARRANTY_DURATION_PARTS_YRS" };
            var written = new HashSet<string>(
                CobieFieldMap.ComponentColumns.Values, StringComparer.OrdinalIgnoreCase);

            foreach (var p in required)
                Assert.True(written.Contains(p),
                    p + " is required at rung 500 by the KUT LOD overlay but no COBie Component " +
                    "column imports into it, so a handover file cannot satisfy the close-out " +
                    "gate on the elements the gate examines.");
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
            Assert.Empty(CobieFieldMap.TypeReadOrder(column));
        }
    }
}
