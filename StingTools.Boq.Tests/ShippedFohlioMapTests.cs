using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // The shipped KUT Fohlio overlay decides how FF&E is carried in a priced bill.
    // It is a data file, so nothing about it is compile-checked: Newtonsoft leaves an
    // unrecognised or wrongly-typed key at its default, and "ffe" silently becoming the
    // fallback looks identical to "ffe" being read.
    //
    // What this DOES prove: the keys FohlioMap reads are present and spelled as it
    // expects, they carry the right JSON types, the treatment value is one the code
    // recognises rather than a typo that normalises to the default by accident, and the
    // cost columns the BOQ rate provider depends on exist.
    //
    // Since the KUT deep review (2026-10) FohlioMap's data half is Revit-free
    // (ExLink/FohlioMapData.cs) and linked here, so the shipped file is ALSO read through the
    // real class — which is how the list-append defect was found: reading a JObject never
    // exercised the POCO, and the POCO appended the file's columns to its defaults.
    public class ShippedFohlioMapTests
    {
        private static JObject Load()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", "kut_fohlio_map.json");
            Assert.True(File.Exists(path), $"Shipped KUT Fohlio map not found at {path}");
            return JObject.Parse(File.ReadAllText(path));
        }

        [Fact]
        public void BoqTreatment_IsAStringTheCodeRecognises()
        {
            var o = Load();
            var t = o["BoqTreatment"];
            Assert.NotNull(t);
            Assert.Equal(JTokenType.String, t.Type);

            string raw = (string)t;
            // Round-tripping through the real normaliser is the point: a typo like "FFE "
            // or "ffee" would also come back as Ffe (it is the safe default), so assert
            // the raw value is one of the four canonical tokens as well.
            Assert.Equal(FfeTreatment.Ffe, FfeTreatment.Normalize(raw));
            Assert.Contains(raw, new[] { FfeTreatment.Ffe, FfeTreatment.PcSum, FfeTreatment.Measured, FfeTreatment.Excluded });
        }

        [Fact]
        public void BoqTreatmentByCategory_IsAnObject_NotAnArray()
        {
            var o = Load();
            var byCat = o["BoqTreatmentByCategory"];
            Assert.NotNull(byCat);
            // An array here would deserialise to null and silently disable every
            // per-category override.
            Assert.Equal(JTokenType.Object, byCat.Type);
            // Whatever overrides it carries must be values the code recognises.
            foreach (var pr in ((JObject)byCat).Properties())
            {
                Assert.Equal(JTokenType.String, pr.Value.Type);
                string v = (string)pr.Value;
                // The RAW value, not Normalize(v): Normalize maps any typo to a canonical
                // token, so asserting on it could never fail.
                Assert.Contains(v, new[] { FfeTreatment.Ffe, FfeTreatment.PcSum, FfeTreatment.Measured, FfeTreatment.Excluded });
            }
        }

        [Fact]
        public void CostColumns_ArePresent_SoTheRateProviderHasSomethingToRead()
        {
            var o = Load();
            var cols = o["Columns"] as JArray;
            Assert.NotNull(cols);
            var byParam = cols.OfType<JObject>().ToDictionary(c => (string)c["Param"], c => c);

            // FohlioRateProvider reads these two off the element; Fohlio_Import writes
            // them from these columns. Without them the provider can never fire.
            Assert.True(byParam.ContainsKey("FOHLIO_UNIT_COST_NR"), "Unit-cost column missing");
            Assert.True(byParam.ContainsKey("FOHLIO_CURRENCY_TXT"), "Currency column missing");
            // The import writes the numeric cost via SetDouble, not the text-diff path,
            // but the currency is a normal write-back field.
            Assert.True((bool)byParam["FOHLIO_CURRENCY_TXT"]["WriteBack"]);

            // The link key must stay a write-back field or nothing links back to Fohlio.
            Assert.True(byParam.ContainsKey("FOHLIO_REF_TXT"));
            Assert.True((bool)byParam["FOHLIO_REF_TXT"]["WriteBack"]);
        }

        [Fact]
        public void FfeCategories_AreListed()
        {
            var cats = Load()["Categories"] as JArray;
            Assert.NotNull(cats);
            Assert.NotEmpty(cats);
            // IsFfeCategory matches on these; an empty list would mean no element is ever
            // treated as FF&E and the whole treatment becomes dead.
            Assert.All(cats, c => Assert.False(string.IsNullOrWhiteSpace((string)c)));
        }
            // ── through the real class ────────────────────────────────────────────

        private static StingTools.ExLink.FohlioMap Parsed()
        {
            string path = Path.Combine(System.AppContext.BaseDirectory, "Data", "kut_fohlio_map.json");
            return StingTools.ExLink.FohlioMap.Parse(File.ReadAllText(path));
        }

        [Fact]
        public void TheShippedMapReplacesTheDefaultColumnsInsteadOfAppendingToThem()
        {
            // Old behaviour: DeserializeObject reused the 8 initialised default columns and
            // appended the file's 12, so the KUT export had 20 columns with Item Tag,
            // Manufacturer, Model and Fohlio Ref twice.
            var fileCols = (Load()["Columns"] as JArray).Count;
            var map = Parsed();
            Assert.Equal(fileCols, map.Columns.Count);
            var dupes = map.Columns.GroupBy(c => c.Header).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, "duplicate export columns: " + string.Join(", ", dupes));
        }

        [Fact]
        public void TheShippedCategoryListIsTheProjectsListNotTheDefaultsPlusIt()
        {
            var fileCats = (Load()["Categories"] as JArray).Select(t => (string)t).ToList();
            Assert.Equal(fileCats, Parsed().Categories);
        }

        [Fact]
        public void WithoutAProjectMapNothingIsOwnerFfe()
        {
            // Old behaviour: the defaults answered IsFfeCategory, so a project with no Fohlio
            // billed plumbing fixtures, lighting, casework and specialty equipment as
            // Owner-procured FF&E — outside OH&P and contingency.
            var none = new StingTools.ExLink.FohlioMap();
            Assert.False(none.IsFfeCategory("Plumbing Fixtures"));
            Assert.False(none.IsFfeCategory("Furniture"));
            Assert.True(Parsed().IsFfeCategory("Furniture"));
        }
    }
}
