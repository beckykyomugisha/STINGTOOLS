using System.IO;
using System.Linq;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase 198 — OmniClass Table 21 (Elements) map. Pure logic; no Revit.
    // Reuses the CsiMasterFormat parser/resolver (same CSV grammar) — the
    // "Section" column carries the OmniClass number.
    public class OmniClassMapTests
    {
        private static System.Collections.Generic.List<CsiRule> LoadShipped(string file = "STING_OMNICLASS_21_MAP.csv")
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", file);
            Assert.True(File.Exists(path), $"Shipped OmniClass map not found at {path}");
            return CsiMasterFormat.ParseCsvLines(File.ReadAllLines(path));
        }

        [Fact]
        public void ShippedMap_Loads_And_Every_Row_Has_Code_And_Title()
        {
            var rules = LoadShipped();
            Assert.NotEmpty(rules);
            // Section (OmniClass code) + Title must be present, and the comma-split
            // parser must not have leaked the title into the Nrm2/Unit columns.
            Assert.All(rules, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Section), $"{r.Category} missing OmniClass code");
                Assert.False(string.IsNullOrWhiteSpace(r.Title), $"{r.Category}/{r.Section} missing title");
                Assert.True(string.IsNullOrEmpty(r.Nrm2), $"{r.Category}/{r.Section} leaked into Nrm2 — a comma in the Title?");
                Assert.StartsWith("21-", r.Section);   // Table 21 (Elements)
            });
        }

        [Fact]
        public void Resolves_Services_To_The_Right_Element_Group()
        {
            var rules = LoadShipped();
            // HVAC ducts → 21-04 30 00; sanitary pipes → plumbing 21-04 20 00;
            // sprinklers → fire protection 21-04 40 00.
            Assert.Equal("21-04 30 00", CsiMasterFormat.Resolve(rules, "Ducts", "", "", "").Section);
            Assert.Equal("21-04 20 00", CsiMasterFormat.Resolve(rules, "Pipes", "", "", "SAN").Section);
            Assert.Equal("21-04 30 00", CsiMasterFormat.Resolve(rules, "Pipes", "", "", "CHW").Section);
            Assert.Equal("21-04 40 00", CsiMasterFormat.Resolve(rules, "Sprinklers", "", "", "").Section);
        }

        [Fact]
        public void Specific_Family_Beats_Generic_For_Mechanical_Equipment()
        {
            var rules = LoadShipped();
            // An elevator on Mechanical Equipment → Conveying; a generic AHU → HVAC.
            Assert.Equal("21-04 10 00", CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "Passenger Elevator", "", "").Section);
            Assert.Equal("21-04 30 00", CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "Generic AHU", "", "").Section);
        }

        [Fact]
        public void Structure_And_Site_Resolve()
        {
            var rules = LoadShipped();
            Assert.Equal("21-02 10 00", CsiMasterFormat.Resolve(rules, "Structural Framing", "", "", "").Section);  // Superstructure
            Assert.Equal("21-01 10 00", CsiMasterFormat.Resolve(rules, "Structural Foundations", "", "", "").Section); // Foundations
            Assert.Equal("21-07 20 00", CsiMasterFormat.Resolve(rules, "Planting", "", "", "").Section);            // Site Improvements
        }

        // ── Phase 199 — table switching ─────────────────────────────────────

        [Fact]
        public void Table13_SpacesMap_ResolvesByRoomName()
        {
            // Table 13 keys on the host-room name fed into FamilyRegex (Category "*").
            var rules = LoadShipped("STING_OMNICLASS_13_MAP.csv");
            Assert.All(rules, r =>
            {
                Assert.StartsWith("13-", r.Section);
                Assert.False(string.IsNullOrWhiteSpace(r.Title));
                Assert.True(string.IsNullOrEmpty(r.Nrm2), "comma leaked into Title?");
            });
            Assert.Equal("13-25 00 00", CsiMasterFormat.Resolve(rules, "*", "Male WC", "Male WC", "").Section);     // Sanitary
            Assert.Equal("13-15 00 00", CsiMasterFormat.Resolve(rules, "*", "Bishop Office", "Bishop Office", "").Section); // Office
            Assert.Equal("13-21 00 00", CsiMasterFormat.Resolve(rules, "*", "Main Corridor", "Main Corridor", "").Section); // Circulation
            Assert.Equal("13-61 00 00", CsiMasterFormat.Resolve(rules, "*", "Sealing Room", "Sealing Room", "").Section);   // Religious
            Assert.Null(CsiMasterFormat.Resolve(rules, "*", "", "", ""));   // no room → unresolved
        }

        [Fact]
        public void TableRegistry_KnowsElementVsSpatial()
        {
            Assert.False(OmniClassTables.Resolve("21").IsSpatial);
            Assert.Equal("STING_OMNICLASS_21_MAP.csv", OmniClassTables.Resolve("21").MapFile);
            // Both Spaces tables (13, 14) are spatial — they classify the host room.
            Assert.True(OmniClassTables.Resolve("13").IsSpatial);
            Assert.True(OmniClassTables.Resolve("14").IsSpatial);
            Assert.Equal("Table 13 — Spaces by Function", OmniClassTables.Resolve("13").Label);
            // The other BOQ-relevant element axes are registered + non-spatial.
            Assert.False(OmniClassTables.Resolve("23").IsSpatial);
            Assert.Equal("Products", OmniClassTables.Resolve("23").Name);
            Assert.Equal("Work Results", OmniClassTables.Resolve("22").Name);
            Assert.Equal("Materials", OmniClassTables.Resolve("41").Name);
            Assert.False(OmniClassTables.Resolve("11").IsSpatial);
            // A truly-unknown number → generic non-spatial info (so an overlay table still works).
            Assert.Equal("STING_OMNICLASS_99_MAP.csv", OmniClassTables.Resolve("99").MapFile);
            Assert.False(OmniClassTables.Resolve("99").IsSpatial);
            // Blank → default Table 21.
            Assert.Equal("21", OmniClassTables.Resolve("").Number);
        }

        [Fact]
        public void TableRegistry_TableOf_ReadsPrefix()
        {
            Assert.Equal("21", OmniClassTables.TableOf("21-04 30 00"));
            Assert.Equal("13", OmniClassTables.TableOf("13-25 00 00"));
            Assert.Equal("", OmniClassTables.TableOf(""));
        }

        [Fact]
        public void Policy_OmniClassTable_ParsesAndNormalises()
        {
            // Default policy → Table 21.
            Assert.Equal("21", ClassificationPolicy.Default.OmniClassTableNumber);
            // A policy that sets ONLY the table (no order) keeps it + uses the default order.
            var p = ClassificationPolicy.Parse("{ \"omniClassTable\": \"Table 13\" }");
            Assert.Equal("13", p.OmniClassTableNumber);
            Assert.NotEmpty(p.Order);
            // Normalises "T23" / whitespace → "23".
            Assert.Equal("23", ClassificationPolicy.Parse("{ \"omniClassTable\": \"T23\" }").OmniClassTableNumber);
        }

        // ── Phase 199c — Table 23 (Products) + Table 41 (Materials) maps ────

        [Fact]
        public void Table23_ProductsMap_ResolvesFromAutodeskCodes()
        {
            var rules = LoadShipped("STING_OMNICLASS_23_MAP.csv");
            Assert.All(rules, r =>
            {
                Assert.StartsWith("23-", r.Section);
                Assert.False(string.IsNullOrWhiteSpace(r.Title));
                Assert.True(string.IsNullOrEmpty(r.Nrm2), "comma leaked into Title?");
            });
            Assert.Equal("23-17 11 00", CsiMasterFormat.Resolve(rules, "Doors", "", "", "").Section);
            Assert.Equal("23-17 13 00", CsiMasterFormat.Resolve(rules, "Windows", "", "", "").Section);
            Assert.Equal("23-33 49 00", CsiMasterFormat.Resolve(rules, "Ducts", "", "", "").Section);          // HVAC Ductwork
            Assert.Equal("23-27 39 00", CsiMasterFormat.Resolve(rules, "Pipes", "", "", "DCW").Section);        // Piping
            Assert.Equal("23-29 33 00", CsiMasterFormat.Resolve(rules, "Pipes", "", "", "FP").Section);         // Fire suppression
            Assert.Equal("23-35 47 00", CsiMasterFormat.Resolve(rules, "Lighting Fixtures", "", "", "").Section);
            // Family-specific refinement beats the Mechanical Equipment family default.
            Assert.Equal("23-33 25 00", CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "Generic AHU", "", "").Section);
            Assert.Equal("23-33 00 00", CsiMasterFormat.Resolve(rules, "Mechanical Equipment", "Misc Unit", "", "").Section);
            Assert.Equal("23-31 19 00", CsiMasterFormat.Resolve(rules, "Plumbing Fixtures", "Wall Hung WC", "", "").Section); // Toilets
        }

        [Fact]
        public void Table41_MaterialsMap_ResolvesByTypeNameKeyword()
        {
            var rules = LoadShipped("STING_OMNICLASS_41_MAP.csv");
            Assert.All(rules, r =>
            {
                Assert.StartsWith("41-", r.Section);
                Assert.False(string.IsNullOrWhiteSpace(r.Title));
                Assert.True(string.IsNullOrEmpty(r.Nrm2), "comma leaked into Title?");
            });
            // Material keyword lives in the type name (3rd arg = type).
            Assert.Equal("41-30 10 25 19 15", CsiMasterFormat.Resolve(rules, "Floors", "Floor", "Concrete - 200mm", "").Section); // Cement
            Assert.Equal("41-30 20 11 11", CsiMasterFormat.Resolve(rules, "Structural Framing", "UB", "Steel UB 305x165", "").Section); // Carbon Steel
            Assert.Equal("41-30 20 11 14", CsiMasterFormat.Resolve(rules, "Pipes", "Pipe", "Stainless Steel DN50", "").Section); // Stainless beats steel
            Assert.Equal("41-30 10 27 17 13", CsiMasterFormat.Resolve(rules, "Walls", "Curtain", "Glazed Curtain Wall", "").Section); // Glass
            Assert.Equal("41-30 30 11 19 13", CsiMasterFormat.Resolve(rules, "Casework", "Unit", "White Oak Veneer", "").Section); // Hardwood
            // Category fallback when the type name has no material keyword.
            Assert.Equal("41-30 20 11 11", CsiMasterFormat.Resolve(rules, "Structural Rebar", "Rebar", "16mm", "").Section); // Carbon Steel
        }

        // ── Phase 199d — resolver robustness + match modes ──────────────────

        [Fact]
        public void Resolver_IsCaseInsensitive_WithoutInlineFlag()
        {
            // A hand-authored overlay row with no "(?i)" must still match regardless of case.
            var rules = new System.Collections.Generic.List<CsiRule>
            {
                new CsiRule { Category = "Walls", FamilyRegex = "concrete", Section = "21-02 20 00", Title = "X" },
            };
            Assert.NotNull(CsiMasterFormat.Resolve(rules, "Walls", "CONCRETE Cavity", "", ""));
            Assert.NotNull(CsiMasterFormat.Resolve(rules, "Walls", "Precast Concrete", "", ""));
        }

        [Fact]
        public void Resolver_ReportsAmbiguity_WhenTwoRulesTieOnDifferentCodes()
        {
            var rules = new System.Collections.Generic.List<CsiRule>
            {
                new CsiRule { Category = "Specialty Equipment", Section = "23-19 00 00", Title = "A" },
                new CsiRule { Category = "Specialty Equipment", Section = "23-21 00 00", Title = "B" },
            };
            var best = CsiMasterFormat.Resolve(rules, "Specialty Equipment", "", "", "", out int score, out int tie);
            Assert.Equal("23-19 00 00", best.Section);  // first wins
            Assert.Equal(1, score);
            Assert.Equal(2, tie);                        // ambiguous — two distinct codes tie
            // Two rules that tie but AGREE on the code are not flagged.
            var agree = new System.Collections.Generic.List<CsiRule>
            {
                new CsiRule { Category = "Doors", Section = "23-17 11 00" },
                new CsiRule { Category = "Doors", Section = "23-17 11 00" },
            };
            CsiMasterFormat.Resolve(agree, "Doors", "", "", "", out _, out int tie2);
            Assert.Equal(1, tie2);
        }

        [Fact]
        public void TableRegistry_MatchModes()
        {
            Assert.Equal("element",  OmniClassTables.Resolve("21").MatchMode);
            Assert.Equal("element",  OmniClassTables.Resolve("23").MatchMode);
            Assert.Equal("room",     OmniClassTables.Resolve("13").MatchMode);
            Assert.Equal("room",     OmniClassTables.Resolve("14").MatchMode);
            Assert.Equal("material", OmniClassTables.Resolve("41").MatchMode);
            Assert.Equal("element",  OmniClassTables.Resolve("99").MatchMode);   // generic fallback
            Assert.True(OmniClassTables.Resolve("13").IsSpatial);
            Assert.False(OmniClassTables.Resolve("41").IsSpatial);               // material ≠ spatial
        }

        [Fact]
        public void TableRegistry_All_ListsEveryTable_FlagsMappedOnes()
        {
            var all = OmniClassTables.All;
            Assert.Equal(15, all.Count);                              // the real OmniClass tables
            Assert.Equal(new[] { "11", "12", "13", "14", "21" },     // ordered by number
                all.Take(5).Select(t => t.Number).ToArray());
            // Only 21/23/41/13 ship a corporate map out of the box.
            Assert.True(OmniClassTables.ShipsMap("21"));
            Assert.True(OmniClassTables.ShipsMap("23"));
            Assert.True(OmniClassTables.ShipsMap("41"));
            Assert.True(OmniClassTables.ShipsMap("13"));
            Assert.False(OmniClassTables.ShipsMap("32"));
            Assert.False(OmniClassTables.ShipsMap("99"));
            Assert.Equal(4, OmniClassTables.MappedTableNumbers.Count);
        }
    }
}
