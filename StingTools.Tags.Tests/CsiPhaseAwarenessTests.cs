using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-5 — Division 02 Existing Conditions, keyed on the element's PHASE rather than on
    /// its name.
    ///
    /// <para>The withdrawn first pass keyed Division 02 on naming, and <b>nobody names a
    /// toposolid "demolition"</b>, so those rows could not fire on a real model — they would
    /// have read as coverage in a review while delivering nothing. These tests exist to make
    /// the opposite true: the rule fires on the model's own phase data, and the harness at
    /// the bottom measures how much of the map is reachable so a future change cannot quietly
    /// shrink it.</para>
    /// </summary>
    public class CsiPhaseAwarenessTests
    {
        private static List<CsiRule> Shipped()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(
                       dir.FullName, "StingTools", "Data", "STING_CSI_MASTERFORMAT_MAP.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            var rules = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(
                Path.Combine(dir.FullName, "StingTools", "Data", "STING_CSI_MASTERFORMAT_MAP.csv")));
            Assert.True(rules.Count > 100, "CSI map looks empty: " + rules.Count);
            return rules;
        }

        private static string Section(string cat, string family, string type, string sys, string phase)
        {
            var r = CsiMasterFormat.Resolve(Shipped(), cat, family, type, sys, phase);
            Assert.True(r != null, $"no rule resolved for {cat} / '{family}' / phase='{phase}'");
            return CsiMasterFormat.NormalizeSection(r.Section);
        }

        // ── ElementPhaseState — the classification rules ─────────────────────────

        [Theory]
        // created, demolished, phaseCount  ->  state
        [InlineData(1, -1, 2, ElementPhaseState.New)]
        [InlineData(0, -1, 1, ElementPhaseState.New)]        // single-phase: everything is New
        [InlineData(0, -1, 2, ElementPhaseState.Existing)]   // built before the works
        [InlineData(0, 1, 2, ElementPhaseState.Demolished)]  // pre-existing, removed
        [InlineData(1, 2, 3, ElementPhaseState.Temporary)]   // built AND removed by the works
        public void PhaseStateClassification(int created, int demolished, int count, string expected)
            => Assert.Equal(expected, ElementPhaseState.Classify(created, demolished, count));

        /// <summary>
        /// <b>The single-phase trap.</b> In a one-phase project every element sits in phase 0
        /// and none of it is pre-existing — that phase IS the works. Classifying it Existing
        /// would silently mark an entire building as out of scope, and the bill would be
        /// empty for a reason nobody would look for.
        /// </summary>
        [Fact]
        public void ASinglePhaseProjectHasNoExistingElements()
        {
            Assert.Equal(ElementPhaseState.New, ElementPhaseState.Classify(0, -1, 1));
            Assert.NotEqual(ElementPhaseState.Existing, ElementPhaseState.Classify(0, -1, 1));
        }

        /// <summary>An element with no phase information is UNKNOWN, not New. A grid or a
        /// level must never satisfy a phase rule — and "New" would be a fact we do not
        /// have.</summary>
        [Theory]
        [InlineData(-1, -1, 3)]
        [InlineData(-1, -1, 1)]
        [InlineData(0, -1, 0)]      // no phases readable at all
        public void NotPhaseAwareIsUnknownNotNew(int created, int demolished, int count)
            => Assert.Equal(ElementPhaseState.Unknown, ElementPhaseState.Classify(created, demolished, count));

        [Fact]
        public void UnknownIsTheEmptyStringSoScoreTreatsItAsNoMatch()
        {
            Assert.Equal("", ElementPhaseState.Unknown);
            var rule = new CsiRule { Category = "*", Phase = ElementPhaseState.Demolished, Section = "02 41 19" };
            Assert.Equal(-1, rule.Score("Walls", "", "", "", ElementPhaseState.Unknown));
            Assert.Equal(-1, rule.Score("Walls", "", "", "", null));
        }

        // ── The rule fires, and outranks the product rules ───────────────────────

        /// <summary>The headline. A demolished element classifies as Division 02 whatever it
        /// is — that is what "Existing Conditions" means, and it is what the naming-keyed
        /// rows could never do.</summary>
        [Theory]
        [InlineData("Walls", "", "Blockwork 200")]
        [InlineData("Structural Framing", "Precast Beam 400", "Precast Beam 400")]
        [InlineData("Doors", "Wood Door", "Single Flush")]
        [InlineData("Mechanical Equipment", "AHU-01", "Air Handling Unit")]
        [InlineData("Toposolid", "", "Existing Ground")]
        [InlineData("Pipes", "", "Pipe Types")]
        public void ADemolishedElementIsDivision02WhateverItIs(string cat, string fam, string type)
            => Assert.Equal("024119", Section(cat, fam, type, "", ElementPhaseState.Demolished));

        /// <summary>…and the same element NOT demolished keeps its product classification.
        /// The phase rule must not leak into ordinary work.</summary>
        [Theory]
        [InlineData(ElementPhaseState.New)]
        [InlineData(ElementPhaseState.Existing)]
        [InlineData(ElementPhaseState.Temporary)]
        [InlineData(ElementPhaseState.Unknown)]
        public void EveryOtherStateKeepsTheProductClassification(string phase)
        {
            string s = Section("Structural Framing", "Precast Beam 400", "Precast Beam 400", "", phase);
            Assert.NotEqual("024119", s);
            Assert.Equal("034100", s);   // Precast Structural Concrete, as before
        }

        /// <summary>Weighting, asserted directly: a phase-qualified rule must beat any
        /// combination of the product qualifiers. Without this, the Division 02 rule would
        /// fire only where nothing else matched.</summary>
        [Fact]
        public void PhaseOutranksEveryProductQualifierCombined()
        {
            var demo = new CsiRule { Category = "*", Phase = ElementPhaseState.Demolished, Section = "02 41 19" };
            var mostSpecificProduct = new CsiRule
            {
                Category = "Structural Framing",
                FamilyRegex = "(?i)precast",
                TypeRegex = "(?i)beam",
                Sys = "STR",
                Section = "03 41 00",
            };
            int d = demo.Score("Structural Framing", "Precast Beam", "Precast Beam", "STR", ElementPhaseState.Demolished);
            int pmax = mostSpecificProduct.Score("Structural Framing", "Precast Beam", "Precast Beam", "STR", ElementPhaseState.Demolished);
            Assert.True(d > pmax,
                $"a phase rule scored {d} and the most specific product rule {pmax}; Division 02 would lose");
        }

        /// <summary>The shipped Division 02 row carries NRM2 <b>1</b> — "Demolitions" in THIS
        /// repository's section vocabulary (<c>BOQCostManager.GuessSectionName</c>), which is
        /// not the published NRM2 numbering. Pinned because guessing across that gap moves
        /// money.</summary>
        [Fact]
        public void TheDivision02RowBillsUnderTheInHouseDemolitionsSection()
        {
            var rule = CsiMasterFormat.Resolve(Shipped(), "Walls", "", "Blockwork", "", ElementPhaseState.Demolished);
            Assert.Equal("024119", CsiMasterFormat.NormalizeSection(rule.Section));
            Assert.Equal("1", rule.Nrm2);
        }

        /// <summary>Existing and Temporary are deliberately unclassified: no shipped rule
        /// names either. A rule for Existing would assert a bill item for work nobody is
        /// doing.</summary>
        [Fact]
        public void NoShippedRuleClassifiesExistingOrTemporary()
        {
            var offenders = Shipped()
                .Where(r => string.Equals(r.Phase, ElementPhaseState.Existing, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(r.Phase, ElementPhaseState.Temporary, StringComparison.OrdinalIgnoreCase))
                .Select(r => $"{r.Category} / phase={r.Phase} -> {r.Section}")
                .ToList();
            Assert.True(offenders.Count == 0,
                "Existing-to-remain is not work and temporary works are a QS judgement:\n  "
                + string.Join("\n  ", offenders));
        }

        // ── Backward compatibility — the hazard §7 names ─────────────────────────

        /// <summary>
        /// <b>A 6-column project overlay still loads AND still matches.</b>
        ///
        /// <para>This is the reason the row-shape change was not made before.
        /// <c>ParseCsvLines</c> DROPS a row with fewer fields than it demands — silently, no
        /// error anywhere — so widening the row without keeping the trailing columns optional
        /// would take every project's <c>_BIM_COORD/csi_map.csv</c> offline and look exactly
        /// like a map with no opinion.</para>
        /// </summary>
        [Fact]
        public void SixColumnOverlaysStillLoadAndStillMatch()
        {
            var rules = CsiMasterFormat.ParseCsvLines(new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
                "Walls,,(?i)blockwork,,04 22 00,Concrete Unit Masonry",
                "Doors,(?i)wood,,,08 14 00,Wood Doors",
            });

            Assert.Equal(2, rules.Count);                       // loaded
            Assert.All(rules, r => Assert.Equal("", r.Phase));  // no phase qualifier

            // …and they still RESOLVE, which is the half a load-count would miss.
            Assert.Equal("042200", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Walls", "", "Blockwork 200", "").Section));
            Assert.Equal("081400", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Doors", "Wood Door", "", "").Section));
        }

        /// <summary>Every intermediate width loads too — 7 (Nrm2), 8 (Unit), 9 (the reserved
        /// column) and 10 (Phase).</summary>
        [Fact]
        public void EveryColumnWidthFromSixToTenLoads()
        {
            var rules = CsiMasterFormat.ParseCsvLines(new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
                "Walls,,(?i)a,,04 22 00,Six",
                "Walls,,(?i)b,,04 22 00,Seven,14",
                "Walls,,(?i)c,,04 22 00,Eight,14,m2",
                "Walls,,(?i)d,,04 22 00,Nine,14,m2,",
                "Walls,,(?i)e,,02 41 19,Ten,1,,,Demolished",
            });
            Assert.Equal(5, rules.Count);
            Assert.Equal("", rules[0].Nrm2);
            Assert.Equal("14", rules[1].Nrm2);
            Assert.Equal("m2", rules[2].Unit);
            Assert.Equal("", rules[3].Phase);
            Assert.Equal("Demolished", rules[4].Phase);
        }

        /// <summary>The four-argument overloads are unchanged, so every caller that has not
        /// been taught about phases keeps its answer.</summary>
        [Fact]
        public void ThePhaseFreeOverloadsAreUnchanged()
        {
            var rules = Shipped();
            var a = CsiMasterFormat.Resolve(rules, "Structural Framing", "Precast Beam", "Precast Beam", "");
            var b = CsiMasterFormat.Resolve(rules, "Structural Framing", "Precast Beam", "Precast Beam", "", "");
            Assert.Equal(a.Section, b.Section);
            Assert.Equal("034100", CsiMasterFormat.NormalizeSection(a.Section));
        }

        // ── The resolution harness §7 asks for ───────────────────────────────────

        /// <summary>
        /// A committed resolution harness: every category the shipped map names, resolved in
        /// each phase state, asserting that <b>nothing falls through to no rule at all</b> and
        /// that the Division 02 coverage is exactly what it should be.
        ///
        /// <para>There was no such harness to "re-run" — the map's reachability had never been
        /// measured. This measures it against the real resolver, so a row deleted or a weight
        /// changed shows up as a number instead of as a silent gap on someone's bill.</para>
        /// </summary>
        [Fact]
        public void ResolutionHarness_EveryNamedCategoryResolvesInEveryPhaseState()
        {
            var rules = Shipped();
            var states = new[]
            {
                ElementPhaseState.New, ElementPhaseState.Existing,
                ElementPhaseState.Demolished, ElementPhaseState.Temporary,
                ElementPhaseState.Unknown,
            };
            var categories = rules.Select(r => r.Category)
                                  .Where(c => !string.IsNullOrEmpty(c) && c != "*")
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
            Assert.True(categories.Count > 20, "too few categories to be a harness: " + categories.Count);

            var unresolved = new List<string>();
            int demolitionHits = 0, probes = 0;

            foreach (string cat in categories)
                foreach (string state in states)
                {
                    probes++;
                    // No family/type/sys: the bare category probe, which is the weakest case
                    // and therefore the one that exposes a missing fallback.
                    var r = CsiMasterFormat.Resolve(rules, cat, "", "", "", state);
                    if (r == null)
                    {
                        unresolved.Add($"{cat} / phase='{(state.Length == 0 ? "(unknown)" : state)}'");
                        continue;
                    }
                    if (CsiMasterFormat.NormalizeSection(r.Section).StartsWith("0241")) demolitionHits++;
                }

            // ── The baseline, and why it is not zero ─────────────────────────────
            //
            // FOUND BY THIS HARNESS, PRE-EXISTING, AND DELIBERATELY NOT FIXED HERE:
            // two categories have NO bare-category fallback row. Every rule on them carries
            // a qualifier — Generic Models are all FamilyRegex-keyed, Pipes are all SYS-keyed
            // — so an unqualified element in either resolves to nothing and its CSI section
            // is left blank.
            //
            // Authoring a fallback for them is a classification judgement, not a repair:
            // "Generic Models" is Revit's word for "we did not say what this is", and a
            // catch-all section would put a confident CSI number on exactly the elements
            // nobody has classified. A pipe with no SYS token has the same problem — 22 11 00
            // and 23 21 00 are both plausible and only the service decides.
            //
            // So the set is BASELINED rather than asserted empty: it cannot grow silently,
            // and shrinking it means deleting the entry in the same commit that fixes it.
            var knownUnresolvable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Generic Models",
                "Pipes",
            };

            var unexpected = unresolved
                .Where(u => !knownUnresolvable.Contains(u.Split('/')[0].Trim()))
                .ToList();
            Assert.True(unexpected.Count == 0,
                $"{unexpected.Count} of {probes} probes resolved to NO rule, outside the known baseline:\n  "
                + string.Join("\n  ", unexpected));

            var stillFailing = new HashSet<string>(
                unresolved.Select(u => u.Split('/')[0].Trim()), StringComparer.OrdinalIgnoreCase);
            var fixedSince = knownUnresolvable.Where(c => !stillFailing.Contains(c)).ToList();
            Assert.True(fixedSince.Count == 0,
                "these now resolve — remove them from knownUnresolvable in this same commit, "
                + "so the baseline only ever shrinks: " + string.Join(", ", fixedSince));

            // Exactly one probe per category lands in Division 02: the Demolished one. This
            // holds even for the two baselined categories, because the "*" phase rule matches
            // any category — which is itself the point of keying on state rather than name.
            Assert.Equal(categories.Count, demolitionHits);
        }
    }
}
