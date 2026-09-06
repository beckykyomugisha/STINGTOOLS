using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    // ClassificationReader.Read() reads five parameters and returns a ClassificationInfo
    // built from them. On main, NONE of the five existed — not in MR_PARAMETERS.txt, not
    // in the .csv mirror, not in CATEGORY_BINDINGS.csv, not in RESOLVED_BINDINGS.csv — so
    // every read returned "" and HasAnyUniclass was always false.
    //
    // Nothing failed. An absent parameter reads exactly like an empty one, so the whole
    // Uniclass / NBS / RFI axis was dead in a way no build, no test and no gate could
    // see: BOQ grouping fell through to the next rung, COBie and IFC export carried no
    // classification, and the "Uniclass 2015" option in the standard picker selected an
    // order whose first three rungs could never match.
    //
    // These assertions exist so that cannot silently return. They read the SHIPPED files,
    // because none of this is compile-checked.
    public class ClassificationParameterBindingTests
    {
        // The five, with the GUIDs now shipped. Written out rather than read from
        // ClassificationReader, which imports the Revit API and cannot be linked here —
        // so this IS the second copy, and its job is to disagree loudly if the first moves.
        private static readonly (string Name, string Guid)[] Required =
        {
            ("UNICLASS_PR_TXT",   "8dfccc8d-fd49-5bc5-9989-e0537a632a51"),
            ("UNICLASS_SS_TXT",   "56d504fd-7f0f-51bd-b1cc-1f38ed9cdfd9"),
            ("UNICLASS_EF_TXT",   "40adcf6e-4d27-5f38-abd8-d2a92cbef48f"),
            ("NBS_CODE_TXT",      "7dd1529e-886b-56b5-96fa-d023f03b6a32"),
            ("ASSET_RFI_URL_TXT", "8c96dd6c-13ec-5f34-9caa-a0d9de7abd19"),
        };

        private static string DataFile(string name)
        {
            string p = Path.Combine(System.AppContext.BaseDirectory, "Data", name);
            Assert.True(File.Exists(p), $"Shipped data file not found at {p}");
            return p;
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void SharedParameterFile_DefinesIt(int i)
        {
            var (name, guid) = Required[i];
            var row = File.ReadLines(DataFile("MR_PARAMETERS.txt"))
                .Where(l => l.StartsWith("PARAM\t"))
                .Select(l => l.Split('\t'))
                .FirstOrDefault(f => f.Length > 3 && f[2] == name);

            Assert.True(row != null,
                $"{name} is not in MR_PARAMETERS.txt — ClassificationReader reads it, so it returns \"\" for every element");
            Assert.Equal(guid, row[1]);
            Assert.Equal("TEXT", row[3]);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void BindingSpec_BindsIt(int i)
        {
            var (name, _) = Required[i];
            var bindings = File.ReadLines(DataFile("RESOLVED_BINDINGS.csv"))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Select(l => l.Split(new[] { ',' }, 2))
                .Where(f => f.Length == 2)
                .ToDictionary(f => f[0].Trim(), f => f[1].Trim(), StringComparer.Ordinal);

            Assert.True(bindings.ContainsKey(name),
                $"{name} has no row in RESOLVED_BINDINGS.csv — SharedParamGuids reads an absent " +
                "param as intentionally UNBOUND, so it exists in the file and on no element");
            // A classification code is a property of the thing, not of a discipline.
            Assert.Equal("<ALL>", bindings[name]);
        }

        [Fact]
        public void MirrorAndDefinitionAgree()
        {
            var csv = File.ReadLines(DataFile("MR_PARAMETERS.csv"))
                .Where(l => !l.StartsWith("#") && l.Trim().Length > 0)
                .Select(l => l.Split(','))
                .Where(f => f.Length > 3)
                .ToDictionary(f => f[1], f => (Guid: f[2], Type: f[3]), StringComparer.Ordinal);

            foreach (var (name, guid) in Required)
            {
                Assert.True(csv.ContainsKey(name), $"{name} is missing from the MR_PARAMETERS.csv mirror");
                Assert.Equal(guid, csv[name].Guid);
                Assert.Equal("TEXT", csv[name].Type);
            }
        }

        [Fact]
        public void TheFiveAreBoundToTheSameCategorySet()
        {
            // ClassificationReader reads all five off one element in one call. If they bind
            // to different sets, an element carries a Uniclass code and no NBS reference,
            // and the reason is invisible — worse than none of them binding, because it
            // looks like the data is simply missing.
            var byParam = File.ReadLines(DataFile("CATEGORY_BINDINGS.csv"))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Select(l => l.Split(','))
                .Where(f => f.Length >= 2 && f[0] != "Parameter_Name")
                .GroupBy(f => f[0])
                .ToDictionary(g => g.Key, g => g.Select(f => f[1]).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                              StringComparer.Ordinal);

            foreach (var (name, _) in Required)
                Assert.True(byParam.ContainsKey(name), $"{name} has no CATEGORY_BINDINGS.csv rows");

            var first = byParam[Required[0].Name];
            Assert.NotEmpty(first);
            foreach (var (name, _) in Required.Skip(1))
                Assert.Equal(first, byParam[name]);
        }
    }
}
