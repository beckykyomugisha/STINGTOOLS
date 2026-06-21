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
            Assert.True(OmniClassTables.Resolve("13").IsSpatial);
            Assert.Equal("Table 13 — Spaces by Function", OmniClassTables.Resolve("13").Label);
            Assert.False(OmniClassTables.Resolve("23").IsSpatial);
            // Unknown number → generic non-spatial info (so an overlay table still works).
            Assert.Equal("STING_OMNICLASS_36_MAP.csv", OmniClassTables.Resolve("36").MapFile);
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
    }
}
