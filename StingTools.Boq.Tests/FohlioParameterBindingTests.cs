using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    // FohlioRateProvider (rate priority 96) reads FOHLIO_UNIT_COST_NR and
    // FOHLIO_CURRENCY_TXT off the element. Both were declared as C# constants and
    // defined in NONE of the parameter sources, so the provider could never read a value
    // from a parameter — it fell through to the ES import snapshot and, failing that,
    // returned null. Silently: a missing parameter reads as 0, and the provider treats 0
    // as "no Fohlio price" rather than "misconfigured", so Owner-procured FF&E was
    // priced off the rate book with nothing said.
    //
    // These assertions exist so a future unbinding fails here rather than in a tender.
    // They read the SHIPPED data files, which is the only way to catch it: none of this
    // is compile-checked, and a name that drifts between the C# constant and the data
    // file produces no error anywhere.
    public class FohlioParameterBindingTests
    {
        // The three parameters the Fohlio chain depends on, with the GUIDs ParamRegistry
        // ships. Written out rather than read from the C# because ParamRegistry imports
        // the Revit API and cannot be linked here — so this IS the second copy, and its
        // whole job is to disagree loudly if the first one moves.
        private static readonly (string Name, string Guid, string DataType)[] Required =
        {
            ("FOHLIO_REF_TXT",      "0ecf2056-1239-52bc-87f8-17c281e67209", "TEXT"),
            ("FOHLIO_UNIT_COST_NR", "b6e507ab-bab5-5b7f-aecd-ca78bd14f4c5", "NUMBER"),
            ("FOHLIO_CURRENCY_TXT", "f369abea-4433-541c-a409-128ec9c1675e", "TEXT"),
        };

        private static string DataFile(string name)
        {
            string p = Path.Combine(System.AppContext.BaseDirectory, "Data", name);
            Assert.True(File.Exists(p), $"Shipped data file not found at {p}");
            return p;
        }

        // ---- MR_PARAMETERS.txt — the Revit shared-parameter file ------------------
        // A parameter absent here cannot be loaded into a model at all.

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SharedParameterFile_DefinesTheParameter_WithTheGuidTheCodeShips(int i)
        {
            var (name, guid, dataType) = Required[i];
            var row = File.ReadLines(DataFile("MR_PARAMETERS.txt"))
                .Where(l => l.StartsWith("PARAM\t"))
                .Select(l => l.Split('\t'))
                .FirstOrDefault(f => f.Length > 3 && f[2] == name);

            Assert.True(row != null, $"{name} is not defined in MR_PARAMETERS.txt — it cannot be loaded into a model");
            // The GUID is the identity Revit binds on. A regenerated or re-minted GUID
            // orphans every element already carrying the old one.
            Assert.Equal(guid, row[1]);
            Assert.Equal(dataType, row[3]);
        }

        // ---- RESOLVED_BINDINGS.csv — the authoritative binding spec ---------------
        // SharedParamGuids treats a param ABSENT from this file as intentionally
        // UNBOUND, never broad-bound. Absence here is the silent failure, one step
        // later than an absent definition.

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void BindingSpec_BindsTheParameter(int i)
        {
            var (name, _, _) = Required[i];
            var bindings = File.ReadLines(DataFile("RESOLVED_BINDINGS.csv"))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Select(l => l.Split(new[] { ',' }, 2))
                .Where(f => f.Length == 2)
                .ToDictionary(f => f[0].Trim(), f => f[1].Trim(), StringComparer.Ordinal);

            Assert.True(bindings.ContainsKey(name),
                $"{name} has no row in RESOLVED_BINDINGS.csv — SharedParamGuids reads that as " +
                "intentionally UNBOUND, so FohlioRateProvider can never read it");
            Assert.False(string.IsNullOrWhiteSpace(bindings[name]),
                $"{name} has an EMPTY category list, which binds it nowhere");
        }

        [Fact]
        public void CostAndCurrency_BindWhereverTheRefBinds()
        {
            // The three travel together: a link key with no price, or a price with no
            // currency, is a chain that reaches the BOQ and stops. Whatever scope the ref
            // has, the other two need the same one or the join breaks on some categories
            // and not others — which is worse than breaking on all of them, because it
            // looks like it works.
            var bindings = File.ReadLines(DataFile("RESOLVED_BINDINGS.csv"))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Select(l => l.Split(new[] { ',' }, 2))
                .Where(f => f.Length == 2)
                .ToDictionary(f => f[0].Trim(), f => f[1].Trim(), StringComparer.Ordinal);

            string refScope = bindings["FOHLIO_REF_TXT"];
            Assert.Equal(refScope, bindings["FOHLIO_UNIT_COST_NR"]);
            Assert.Equal(refScope, bindings["FOHLIO_CURRENCY_TXT"]);
        }

        // ---- PARAMETER_REGISTRY.json — the curated registry -----------------------

        [Fact]
        public void Registry_CarriesAllThree_WithMatchingGuidsAndTypes()
        {
            var doc = JObject.Parse(File.ReadAllText(DataFile("PARAMETER_REGISTRY.json")));
            var support = (JArray)doc["support_params"];
            Assert.NotNull(support);

            var byName = support.OfType<JObject>()
                .Where(o => o["param_name"] != null)
                .GroupBy(o => (string)o["param_name"])
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var (name, guid, dataType) in Required)
            {
                Assert.True(byName.ContainsKey(name), $"{name} is missing from PARAMETER_REGISTRY.json support_params");
                var e = byName[name];
                Assert.Equal(guid, (string)e["guid"]);
                Assert.Equal(dataType, (string)e["datatype"]);
                // JSON types are not compile-checked: a group_id quoted as a string, or a
                // required flag written as "false", deserialises to a default and goes
                // runtime-dead rather than failing.
                Assert.Equal(JTokenType.Integer, e["group_id"].Type);
                Assert.Equal(JTokenType.Boolean, e["required"].Type);
            }
        }

        // ---- MR_PARAMETERS.csv — the binding/type manifest ------------------------

        [Fact]
        public void ParameterManifest_AgreesWithTheSharedParameterFile()
        {
            var lines = File.ReadLines(DataFile("MR_PARAMETERS.csv"))
                .Where(l => !l.StartsWith("#") && l.Trim().Length > 0)
                .ToList();
            var header = lines[0].Split(',');
            int iName = Array.IndexOf(header, "Parameter_Name");
            int iGuid = Array.IndexOf(header, "GUID");
            int iType = Array.IndexOf(header, "Data_Type");
            Assert.True(iName >= 0 && iType >= 0, "MR_PARAMETERS.csv header shape changed");

            var rows = lines.Skip(1).Select(l => l.Split(',')).ToList();
            foreach (var (name, guid, dataType) in Required)
            {
                var row = rows.FirstOrDefault(f => f.Length > iType && f[iName] == name);
                Assert.True(row != null, $"{name} is missing from MR_PARAMETERS.csv");
                Assert.Equal(dataType, row[iType]);
                if (iGuid >= 0) Assert.Equal(guid, row[iGuid]);
            }
        }

        // ---- the currency decision -------------------------------------------------

        [Fact]
        public void UnitCostIsNUMBER_NotCURRENCY_BecauseTheQuoteIsNotInProjectCurrency()
        {
            // Revit's CURRENCY spec renders with the PROJECT's currency symbol. Fohlio
            // quotes in its own currency — that is the entire reason FOHLIO_CURRENCY_TXT
            // exists beside this — so a CURRENCY-typed parameter would print a USD figure
            // labelled UGX. Currency-pinned params in this repo say so in their name
            // (ASS_CST_UNIT_PRICE_UGX_NR); a multi-currency one must not claim a unit.
            var row = File.ReadLines(DataFile("MR_PARAMETERS.txt"))
                .Where(l => l.StartsWith("PARAM\t"))
                .Select(l => l.Split('\t'))
                .First(f => f.Length > 3 && f[2] == "FOHLIO_UNIT_COST_NR");
            Assert.Equal("NUMBER", row[3]);
            Assert.DoesNotContain("UGX", row[2]);
            Assert.DoesNotContain("USD", row[2]);
        }
    }
}
