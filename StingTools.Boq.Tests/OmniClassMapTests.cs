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
        private static System.Collections.Generic.List<CsiRule> LoadShipped()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", "STING_OMNICLASS_MAP.csv");
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
    }
}
