// ══════════════════════════════════════════════════════════════════════════
//  MaterialRegisterTests.cs — the shipped register, asserted; and the reason it
//  is a CHALLENGER rather than the answer, asserted too.
//
//  The reader had no tests. The 1,279 rows it reads had none either, and they
//  carry the layer build-ups that a project catalogue was seeded WITHOUT — which
//  is how 87 floor types all became one 100 mm layer of concrete.
//
//  The most important assertions in this file are the ones that pin what the
//  register must NOT be allowed to decide. They are written as facts about the
//  shipped data, so that if somebody cleans the class column the tests fail and
//  the refusal can be revisited on evidence rather than on memory.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MaterialRegisterTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "BLE_MATERIALS.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static MaterialRegistry _shipped;

        /// <summary>The register as it SHIPS, read from source rather than a copy — a stale
        /// copy passing while the shipped file is wrong is the failure this exists to stop.</summary>
        private static MaterialRegistry Shipped()
            => _shipped ??= MaterialRegistry.Parse(
                   File.ReadAllText(Path.Combine(DataDir(), "BLE_MATERIALS.csv")),
                   File.ReadAllText(Path.Combine(DataDir(), "MEP_MATERIALS.csv")));

        // ══════════════════════════════════════════════════════════════════════
        //  1. It reads the whole register
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Whole_Register_Loads_With_No_Warnings()
        {
            var r = Shipped();
            Assert.Equal(1279, r.Count);
            Assert.Equal(815, r.Rows.Count(x => x.Source == "BLE"));
            Assert.Equal(464, r.Rows.Count(x => x.Source == "MEP"));

            // Checked rather than assumed, because the brief said to check: there are NO
            // duplicate MAT_NAMEs, within either file or across the two. If that ever
            // changes the loader reports it instead of last-wins, and this fails.
            Assert.True(r.Warnings.Count == 0,
                "register load warnings:\n  " + string.Join("\n  ", r.Warnings));
            Assert.Equal(1279, r.Rows.Select(x => x.Name.Trim().ToUpperInvariant()).Distinct().Count());
        }

        [Fact]
        public void Every_Row_Carries_An_Identity_Class_And_A_Name()
        {
            var r = Shipped();
            Assert.Empty(r.Rows.Where(x => string.IsNullOrWhiteSpace(x.Name)));
            Assert.Empty(r.Rows.Where(x => string.IsNullOrWhiteSpace(x.IdentityClass)));
        }

        [Fact]
        public void The_Layer_Columns_Are_Read_And_Not_Discarded()
        {
            // The whole point. Whatever seeded 87 single-layer floor types from this file
            // used the NAME column and dropped these.
            var r = Shipped();
            var flr = r.Rows.Where(x => x.Code.StartsWith("FLR-", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(95, flr.Count);
            Assert.Equal(95, flr.Count(x => x.Layers.Count >= 1));   // every FLR row declares layer 1
            Assert.Equal(28, flr.Count(x => x.Layers.Count >= 2));

            var screed = r.ByName("STANDARD CEMENT SCREED 50MM");
            Assert.NotNull(screed);
            Assert.Equal("FLR-001", screed.Code);
            Assert.Single(screed.Layers);
            Assert.Equal("CEMENT SCREED", screed.Layers[0].Material);
            Assert.Equal(50.0, screed.Layers[0].ThicknessMm);
            Assert.Equal(50.0, screed.ThicknessMm);
            Assert.Contains("SUBSTRATE", screed.Layers[0].Function);
        }

        [Fact]
        public void Lookup_Is_By_Trimmed_Case_Insensitive_Name()
        {
            var r = Shipped();
            Assert.NotNull(r.ByName("  solid bamboo 14mm "));
            Assert.Equal("FLR-028", r.ByName("SOLID BAMBOO 14MM").Code);
            Assert.Null(r.ByName("A MATERIAL THAT DOES NOT EXIST"));
            Assert.Null(r.ByName(null));
            Assert.Null(r.ByName("   "));
        }

        [Fact]
        public void A_Missing_File_Is_Reported_Not_Silently_Empty()
        {
            // "not deployed" and "deployed and empty" are different problems, and a
            // register that answered "not in the register" to everything without saying
            // why is how a coverage figure quietly halves.
            var r = MaterialRegistry.Parse(null, null);
            Assert.Equal(0, r.Count);
            Assert.Equal(2, r.Warnings.Count);
            Assert.Contains(r.Warnings, w => w.Contains("BLE_MATERIALS.csv is not deployed"));
            Assert.Contains(r.Warnings, w => w.Contains("MEP_MATERIALS.csv is not deployed"));
        }

        [Fact]
        public void A_Duplicate_Name_Is_Reported_And_The_First_Wins()
        {
            // Not last-wins, and not silent. Two rows claiming one name is a data question.
            const string header = "MAT_CODE,MAT_NAME,MAT_CATEGORY,MAT_THICKNESS_MM,BLE_APP-IDENTITY-CLASS";
            var r = MaterialRegistry.Parse(
                header + "\nBLE-1,SHARED NAME,Walls,100,Concrete\n",
                header + "\nMEP-1,SHARED NAME,Pipes,50,Metal\n");

            Assert.Equal(2, r.Count);
            Assert.Single(r.Warnings);
            Assert.Contains("duplicate MAT_NAME 'SHARED NAME'", r.Warnings[0]);
            Assert.Contains("BLE-1", r.Warnings[0]);
            Assert.Contains("MEP-1", r.Warnings[0]);
            Assert.Equal("BLE-1", r.ByName("SHARED NAME").Code);
        }

        [Fact]
        public void Columns_Are_Located_By_Name_So_A_New_Column_Does_Not_Shift_Them()
        {
            // These files carry 71 columns and grow. A positional reader that shifted by
            // one would return a colour where a class belongs, and every value would still
            // look like a plausible string.
            var r = MaterialRegistry.Parse(
                "BLE_APP-IDENTITY-CLASS,MAT_NAME,SOMETHING_NEW,MAT_CODE\n"
              + "Concrete,A MATERIAL,ignored,X-1\n", null);
            var row = r.ByName("A MATERIAL");
            Assert.NotNull(row);
            Assert.Equal("X-1", row.Code);
            Assert.Equal("Concrete", row.IdentityClass);
        }

        [Fact]
        public void A_Comma_Inside_A_Quoted_Cell_Does_Not_Shift_The_Row()
        {
            var r = MaterialRegistry.Parse(
                "MAT_CODE,MAT_NAME,MAT_SPECIFICATIONS,BLE_APP-IDENTITY-CLASS\n"
              + "X-1,\"BOARD, 12MM\",\"C25/30, 20mm agg\",Concrete\n", null);
            var row = r.ByName("BOARD, 12MM");
            Assert.NotNull(row);
            Assert.Equal("Concrete", row.IdentityClass);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The class vocabulary, and what is deliberately not mapped
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Registers_Class_Vocabulary_Is_Exactly_What_Is_Accounted_For()
        {
            // Neither list is a superset of the other, so an unaccounted-for word would
            // silently become "names no substance" — a shrug arriving as a refusal.
            var words = Shipped().Rows.Select(r => r.IdentityClass.Trim())
                                      .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(new[]
            {
                "Carpet", "Ceiling", "Concrete", "Fabric", "Flooring", "Generic", "Glass",
                "Insulation", "Lining", "Masonry", "Metal", "Paint", "Plaster", "Plastic", "Wood",
            }, words);

            foreach (string w in words)
            {
                bool mapped = MaterialRegistry.ToRevitClass(w) != null;
                bool refused = MaterialRegistry.ClassesThatNameNoSubstance
                                   .Contains(w, StringComparer.OrdinalIgnoreCase);
                Assert.True(mapped ^ refused,
                    $"'{w}' is neither mapped to a Revit class nor listed as naming no substance.");
            }
        }

        [Theory]
        [InlineData("Plaster", "Gypsum")]
        [InlineData("Carpet", "Textile")]
        [InlineData("Fabric", "Textile")]
        [InlineData("Concrete", "Concrete")]
        [InlineData("Masonry", "Masonry")]
        [InlineData("Metal", "Metal")]
        [InlineData("Wood", "Wood")]
        [InlineData("Glass", "Glass")]
        [InlineData("Plastic", "Plastic")]
        [InlineData("Paint", "Paint")]
        [InlineData("Insulation", "Insulation")]
        public void A_Register_Class_That_Names_A_Substance_Translates(string register, string revit)
            => Assert.Equal(revit, MaterialRegistry.ToRevitClass(register));

        [Theory]
        [InlineData("Ceiling")]
        [InlineData("Flooring")]
        [InlineData("Lining")]
        [InlineData("Generic")]
        public void A_Register_Class_That_Names_A_ROLE_Maps_To_Nothing(string register)
        {
            // Ceiling and Flooring are PLACES; Lining is a position in a build-up; Generic
            // is the absence of an answer. 504 of the 1,279 rows carry one of these, and a
            // mapping that guessed would import 504 shrugs into the one controlled field
            // the carbon and cost engines trust.
            Assert.Null(MaterialRegistry.ToRevitClass(register));
        }

        [Fact]
        public void Half_The_Register_Names_No_Substance_At_All()
        {
            // The scale of it, asserted so the refusal above reads as proportionate rather
            // than fussy.
            var rows = Shipped().Rows;
            int roleOnly = rows.Count(r => MaterialRegistry.ToRevitClass(r.IdentityClass) == null);
            Assert.Equal(504, roleOnly);
            Assert.Equal(775, rows.Count - roleOnly);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. The register challenges; it does not decide
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void The_Register_Is_Recorded_On_The_Proposal_And_Changes_No_Answer()
        {
            var reg = Shipped();

            // A row where the two AGREE.
            var bamboo = MaterialClassPlanner.Plan("SOLID BAMBOO 14MM", "", reg);
            Assert.Equal("Wood", bamboo.ProposedClass);
            Assert.Equal("FLR-028", bamboo.RegisterCode);
            Assert.Equal("Wood", bamboo.RegisterClass);
            Assert.False(bamboo.RegisterDisagrees);

            // A row where the REGISTER is right and the answer still does not move.
            var paint = MaterialClassPlanner.Plan("MASONRY PAINT WHITE", "", reg);
            Assert.Equal("Masonry", paint.ProposedClass);        // the needle table, unchanged
            Assert.Equal("Paint", paint.RegisterClass);
            Assert.True(paint.RegisterDisagrees);

            // A row where the NEEDLES are right and the register classifies by trade.
            var skirting = MaterialClassPlanner.Plan("GRANITE SKIRTING 100MM", "", reg);
            Assert.Equal("Stone", skirting.ProposedClass);
            Assert.Equal("Wood", skirting.RegisterClass);        // a skirting is Wood, whatever it is
            Assert.True(skirting.RegisterDisagrees);
        }

        [Fact]
        public void With_No_Registry_The_Planner_Answers_Exactly_As_Before()
        {
            // The registry is optional and additive. Every existing caller and every row of
            // the 1,815-name corpus must be unaffected, or this became a behaviour change
            // wearing a reader's clothes.
            foreach (string n in new[]
            {
                "SOLID BAMBOO 14MM", "MASONRY PAINT WHITE", "GRANITE SKIRTING 100MM",
                "CEMENT PLASTER FINISH 12MM", "Render Material 128-128-128", "CARPET TILE",
            })
            {
                var withOut = MaterialClassPlanner.Plan(n, "");
                var with = MaterialClassPlanner.Plan(n, "", Shipped());
                Assert.Equal(withOut.ProposedClass, with.ProposedClass);
                Assert.Equal(withOut.Reason, with.Reason);
            }
        }

        [Fact]
        public void An_Existing_Class_Still_Wins_And_The_Register_Is_Still_Recorded()
        {
            // Rule 1 is untouched — but a reviewer looking at an already-classified row is
            // exactly who wants to know the register disagrees, so provenance is recorded on
            // the early return too.
            var p = MaterialClassPlanner.Plan("GRANITE SKIRTING 100MM", "Stone", Shipped());
            Assert.Null(p.ProposedClass);
            Assert.Contains("already classified", p.Reason);
            Assert.Equal("WL-184", p.RegisterCode);
            Assert.Equal("Wood", p.RegisterClass);
        }
    }
}
