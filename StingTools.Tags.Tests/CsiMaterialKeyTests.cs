using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-10 — the structural CSI codes are keyed on MATERIAL, not on the Revit category.
    ///
    /// <para>CSI MasterFormat 2020 divides by <b>work result and material</b>: Division 03
    /// Concrete, 04 Masonry, 05 Metals, 06 Wood. A Revit <c>Structural Framing</c> element can
    /// be any of those, which is exactly why a category-keyed default was a judgement call
    /// rather than a rule.</para>
    ///
    /// <para>These tests drive the SHIPPED map, so a row deleted or re-weighted fails here
    /// instead of quietly re-billing a project.</para>
    /// </summary>
    public class CsiMaterialKeyTests
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

        private static string Section(string cat, string family, string type, string material)
        {
            var r = CsiMasterFormat.Resolve(Shipped(), cat, family, type, "", material);
            Assert.True(r != null, $"no rule resolved for {cat} / fam='{family}' / type='{type}' / mat='{material}'");
            return CsiMasterFormat.NormalizeSection(r.Section);
        }

        // ── Material is the primary key ──────────────────────────────────────────

        /// <summary>The headline: the same Revit category, four materials, four MasterFormat
        /// divisions — which is MasterFormat's own basis rather than the authoring tool's.</summary>
        [Theory]
        [InlineData("Concrete, Cast-in-Place - C30/37", "033000")]   // Division 03
        [InlineData("Precast Concrete - C40/50", "034100")]          // Division 03
        [InlineData("Masonry - Concrete Blockwork", "042000")]       // Division 04
        [InlineData("Metal - Steel - S355JR", "051200")]             // Division 05
        [InlineData("Wood - Softwood Timber C24", "061100")]         // Division 06
        [InlineData("Wood - Glulam GL28h", "061700")]                // Division 06
        public void StructuralFramingFollowsItsMaterial(string material, string expected)
            => Assert.Equal(expected, Section("Structural Framing", "", "", material));

        [Theory]
        [InlineData("Concrete, Cast-in-Place - C32/40", "033000")]
        [InlineData("Metal - Steel - S275", "051200")]
        [InlineData("Masonry - Engineering Brick", "042000")]
        public void StructuralColumnsFollowTheirMaterial(string material, string expected)
            => Assert.Equal(expected, Section("Structural Columns", "", "", material));

        /// <summary><b>A declared material beats a material guessed from a type name.</b> The
        /// shipped map has a <c>Structural Columns</c> row keyed on <c>(?i)steel|\bUC\b|\bW\d|HSS</c>.
        /// A column typed "UC 305x305x97" whose structural material says concrete is CONCRETE —
        /// the type name is a naming convention, the parameter is a declaration.</summary>
        [Fact]
        public void ADeclaredMaterialBeatsAGuessFromTheTypeName()
        {
            Assert.Equal("033000",
                Section("Structural Columns", "UC 305x305x97", "UC 305x305x97", "Concrete, Cast-in-Place - C32/40"));

            // …and with no material declared, the name guess still works. Nothing regressed
            // for a model that does not set the parameter.
            Assert.Equal("051200",
                Section("Structural Columns", "UC 305x305x97", "UC 305x305x97", ""));
        }

        // ── …but a named work result beats a material ────────────────────────────

        /// <summary>
        /// <b>The trap this whole weighting exists for.</b> A bored pile is made of concrete and
        /// is still measured under 31 63 00 Bored Piles, not 03 30 00 Cast-in-Place Concrete.
        ///
        /// <para>Without the work-result twin rows, the concrete material row would outscore the
        /// pile row and every pile in the bill would move division — silently, and plausibly,
        /// because concrete is genuinely what a pile is made of.</para>
        /// </summary>
        [Theory]
        [InlineData("Bored Pile 600 CFA", "Concrete, Cast-in-Place - C32/40", "316300")]
        [InlineData("Driven Pile 350", "Precast Concrete - C40/50", "316200")]
        [InlineData("Sheet Pile Shoring", "Metal - Steel - S355JR", "314000")]
        public void ANamedWorkResultBeatsTheMaterial(string family, string material, string expected)
            => Assert.Equal(expected, Section("Structural Foundations", family, family, material));

        /// <summary>A steel joist is Division 05 <b>21</b> 00, not 05 12 00. Same shape: the work
        /// result is more specific than the material.</summary>
        [Fact]
        public void ASteelJoistIsJoistFramingNotGenericSteelFraming()
            => Assert.Equal("052100",
                Section("Structural Framing", "Steel Joist 400", "Steel Joist 400", "Metal - Steel - S355JR"));

        /// <summary>Precast is a more specific work result than concrete, and stays so even when
        /// the material only says "Concrete".</summary>
        [Fact]
        public void PrecastNamedInTheFamilyBeatsAGenericConcreteMaterial()
            => Assert.Equal("034100",
                Section("Structural Framing", "Precast Beam 400", "Precast Beam 400", "Concrete, Cast-in-Place"));

        // ── The fallback stays a fallback ────────────────────────────────────────

        /// <summary>An element with no declared material and no name to go on still resolves —
        /// to the category default, which is what that default is FOR. This must keep working:
        /// a refusal here would leave the bill with an unclassified line.</summary>
        [Theory]
        [InlineData("Structural Framing", "051200")]
        [InlineData("Structural Columns", "033000")]
        [InlineData("Structural Foundations", "033000")]
        public void NoMaterialFallsBackToTheCategoryDefault(string cat, string expected)
            => Assert.Equal(expected, Section(cat, "", "", ""));

        /// <summary>A material the map does not recognise is NOT a match — it falls back rather
        /// than picking the nearest row. Silently classifying an unknown material as concrete
        /// would be the same guess this change removes.</summary>
        [Fact]
        public void AnUnrecognisedMaterialFallsBackRatherThanGuessing()
            => Assert.Equal("051200", Section("Structural Framing", "", "", "Unobtainium"));

        // ── The weighting, asserted directly ─────────────────────────────────────

        /// <summary>The score ordering the whole design rests on, stated as arithmetic so a
        /// future change to either constant fails here rather than silently re-ranking the map.</summary>
        [Fact]
        public void TheScoreHierarchyIsCategoryThenMaterialThenWorkResult()
        {
            const string cat = "Structural Framing";
            var bare = new CsiRule { Category = cat, Section = "05 12 00" };
            var byName = new CsiRule { Category = cat, FamilyRegex = "(?i)joist", Section = "05 21 00" };
            var byMat = new CsiRule { Category = cat, MaterialRegex = "(?i)steel", Section = "05 12 00" };
            var byBoth = new CsiRule { Category = cat, FamilyRegex = "(?i)joist", MaterialRegex = "(?i)steel", Section = "05 21 00" };

            int sBare = bare.Score(cat, "Joist", "Joist", "", "Steel");
            int sName = byName.Score(cat, "Joist", "Joist", "", "Steel");
            int sMat = byMat.Score(cat, "Joist", "Joist", "", "Steel");
            int sBoth = byBoth.Score(cat, "Joist", "Joist", "", "Steel");

            Assert.True(sBare < sName, $"category ({sBare}) must be weaker than a name guess ({sName})");
            Assert.True(sName < sMat, $"a name guess ({sName}) must be weaker than a declared material ({sMat})");
            Assert.True(sMat < sBoth, $"a material ({sMat}) must be weaker than a named work result + material ({sBoth})");
        }

        /// <summary>A rule that names a material does NOT match an element with none. Otherwise
        /// every material row would fire on every unmaterialed element and the fallback would be
        /// unreachable.</summary>
        [Fact]
        public void AMaterialRuleDoesNotMatchAnElementWithoutOne()
        {
            var r = new CsiRule { Category = "Structural Framing", MaterialRegex = "(?i)steel", Section = "05 12 00" };
            Assert.Equal(-1, r.Score("Structural Framing", "", "", "", ""));
            Assert.Equal(-1, r.Score("Structural Framing", "", "", "", null));
        }

        // ── Backward compatibility of the widened row ────────────────────────────

        /// <summary>
        /// <b>A project overlay written against the 6-, 7- or 8-column shape still loads AND still
        /// matches.</b>
        ///
        /// <para><c>ParseCsvLines</c> DROPS a row with fewer fields than it demands — silently, with
        /// no error anywhere — so widening the row to nine columns without keeping the trailing ones
        /// optional would take every project's <c>_BIM_COORD/csi_map.csv</c> offline and look like
        /// the map simply had no opinion.</para>
        /// </summary>
        [Fact]
        public void OverlaysWrittenAgainstEveryEarlierColumnCountStillLoadAndMatch()
        {
            var rules = CsiMasterFormat.ParseCsvLines(new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
                "Walls,,(?i)blockwork,,04 22 00,Concrete Unit Masonry",                 // 6 columns
                "Floors,,(?i)screed,,03 54 00,Cast Underlayment,5",                     // 7 columns
                "Roofs,,(?i)membrane,,07 52 00,Modified Bituminous Roofing,18,m2",       // 8 columns
                "Ceilings,,(?i)plaster,,09 24 00,Cement Plastering,28,m2,(?i)plaster",   // 9 columns
            });

            Assert.Equal(4, rules.Count);

            // Every one of them still RESOLVES, which is the half a load-count would miss.
            Assert.Equal("042200", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Walls", "", "Blockwork 200", "").Section));
            Assert.Equal("035400", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Floors", "", "Screed 75", "").Section));
            Assert.Equal("075200", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Roofs", "", "Membrane Roof", "").Section));
            Assert.Equal("092400", CsiMasterFormat.NormalizeSection(
                CsiMasterFormat.Resolve(rules, "Ceilings", "", "Plaster Ceiling", "", "Plaster").Section));

            // And the shorter rows kept the fields they did carry.
            Assert.Equal("", rules[0].Nrm2);
            Assert.Equal("5", rules[1].Nrm2);
            Assert.Equal("m2", rules[2].Unit);
            Assert.Equal("(?i)plaster", rules[3].MaterialRegex);
        }

        /// <summary>The four-argument overloads still exist and behave exactly as before — every
        /// caller that has not been taught about materials keeps its answer.</summary>
        [Fact]
        public void TheMaterialFreeOverloadsAreUnchanged()
        {
            var rules = Shipped();
            var withoutMaterial = CsiMasterFormat.Resolve(rules, "Structural Columns", "UC 305", "UC 305", "");
            var withEmptyMaterial = CsiMasterFormat.Resolve(rules, "Structural Columns", "UC 305", "UC 305", "", "");
            Assert.Equal(withoutMaterial.Section, withEmptyMaterial.Section);
            Assert.Equal("051200", CsiMasterFormat.NormalizeSection(withoutMaterial.Section));
        }

        /// <summary>Every shipped material row still carries an NRM2 code. The bill derives its
        /// work section from this map, and a material row without one would move a structural
        /// line into the category-derived bucket without saying so.</summary>
        [Fact]
        public void EveryShippedMaterialRowCarriesAnNrm2Code()
        {
            var offenders = Shipped()
                .Where(r => !string.IsNullOrEmpty(r.MaterialRegex) && string.IsNullOrWhiteSpace(r.Nrm2))
                .Select(r => $"{r.Category} / {r.MaterialRegex} -> {r.Section}")
                .ToList();
            Assert.True(offenders.Count == 0,
                "material rows with no NRM2 code:\n  " + string.Join("\n  ", offenders));
        }
    }
}
