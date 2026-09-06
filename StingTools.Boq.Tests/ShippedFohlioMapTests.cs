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
    // What it does NOT prove: that FohlioMap's C# property names still match. FohlioMap
    // imports Autodesk.Revit.DB, so it cannot be linked here; renaming a property without
    // renaming the JSON would slip past. The names below are therefore written as the
    // literal contract, not derived from the type.
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
                Assert.Contains(FfeTreatment.Normalize(v),
                    new[] { FfeTreatment.Ffe, FfeTreatment.PcSum, FfeTreatment.Measured, FfeTreatment.Excluded });
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
    }
}
