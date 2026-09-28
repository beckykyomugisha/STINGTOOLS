// ══════════════════════════════════════════════════════════════════════════
//  Bs7671WireTableLayeringTests.cs — ELEC-21.
//
//  A project can replace, add or remove Appendix 4 capacity tables and override
//  the 4B1 / 4C1 factors in _BIM_COORD/bs7671_wire_tables.json. These tests pin
//  the rules in Core/Electrical/Bs7671TableLayering.cs: whole-table replacement
//  by lookup key, single-source rows unless attested, atomic refusal of an
//  invalid override (no corporate fallback), and project data that never cites
//  itself as BS 7671's own.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Calc;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Bs7671WireTableLayeringTests
    {
        private static readonly int[] Mcb = { 6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125 };

        private static string CorporatePath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json");
        }

        private static JObject Corporate() => JObject.Parse(File.ReadAllText(CorporatePath()));

        /// <summary>A manufacturer-style replacement for Cu / PVC70 / Multicore / method C.</summary>
        private static JObject McTable(bool verified = false, JObject check = null, string id = "MFR-C",
                                       string method = "C", double it25 = 30.0)
        {
            var t = new JObject
            {
                ["id"] = id, ["voltDropTable"] = id + "-VD", ["description"] = "Maker X 70 °C multicore",
                ["conductor"] = "Cu", ["insulation"] = "PVC70", ["cableType"] = "Multicore",
                ["installMethod"] = method, ["maxConductorTempC"] = 70, ["source"] = "Maker X datasheet rev 4",
                ["sizes"] = new JArray(
                    Row(1.5, 21.0, 18.5, 29.0, 25.0, verified),
                    Row(2.5, it25, 26.0, 18.0, 15.0, verified),
                    Row(4.0, 38.0, 34.0, 11.0, 9.5, verified)),
            };
            if (check != null) t["twoSourceCheck"] = check;
            return t;
        }

        private static JObject Row(double csa, double it1, double it3, double mv1, double mv3, bool verified)
            => new JObject
            {
                ["csaMm2"] = csa, ["It_1ph"] = it1, ["It_3ph"] = it3,
                ["mVAm_1ph"] = mv1, ["mVAm_3ph"] = mv3, ["verified"] = verified, ["mvVerified"] = verified,
            };

        private static JObject Override(JObject section, string source = null)
        {
            var o = new JObject
            {
                ["schema"] = Bs7671TableLayering.OverrideSchema,
                ["bs7671Appendix4"] = section,
            };
            if (source != null) o["source"] = source;
            return o;
        }

        private static JObject Tables(params JObject[] t) => new JObject { ["capacityTables"] = new JArray(t) };

        private static Bs7671Data Layer(JObject over) => Bs7671TableLayering.Layer(Corporate(), over, "_BIM_COORD/bs7671_wire_tables.json");

        private static Bs7671SizingInput Pvc(double ib, double ambient = 30, string method = "C") => new Bs7671SizingInput
        {
            DesignCurrentA = ib, VoltageV = 230, Phases = 1, LengthM = 10,
            InstallMethod = method, Insulation = "PVC70", Material = "Cu",
            AmbientTempC = ambient, VdLimitPct = 5, DeviceRatingsA = Mcb,
        };

        // ── no override ─────────────────────────────────────────────────────

        [Fact]
        public void Without_an_override_the_corporate_tables_are_unchanged()
        {
            var d = Layer(null);
            Assert.True(string.IsNullOrEmpty(d.LoadError));
            Assert.False(d.HasProjectLayer);
            Assert.Equal(Bs7671Data.FromJson(Corporate()).Tables.Count, d.Tables.Count);
            var t = d.FindTable("Cu", "PVC70", "C");
            Assert.Equal(Bs7671Origin.Corporate, t.Origin);
            Assert.Equal("Table 4D2A", t.Cite());
        }

        [Fact]
        public void Corporate_sizing_basis_does_not_mention_project_data()
        {
            var r = Bs7671CableSizer.Size(Pvc(20), Layer(null));
            Assert.True(r.Sized, r.Refusal);
            Assert.False(r.ProjectTable);
            Assert.DoesNotContain("PROJECT", r.Basis);
        }

        [Fact]
        public void A_missing_override_file_leaves_the_corporate_layer()
        {
            var d = Bs7671TableLayering.LoadLayered(CorporatePath(), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            Assert.True(string.IsNullOrEmpty(d.LoadError));
            Assert.False(d.HasProjectLayer);
        }

        // ── replacement ─────────────────────────────────────────────────────

        [Fact]
        public void A_supplied_table_replaces_the_corporate_one_by_lookup_key()
        {
            int before = Layer(null).Tables.Count;
            var d = Layer(Override(Tables(McTable())));
            Assert.True(string.IsNullOrEmpty(d.LoadError), d.LoadError);
            Assert.Equal(before, d.Tables.Count);
            var t = d.FindTable("Cu", "PVC70", "C");
            Assert.Equal("MFR-C", t.Id);
            Assert.Equal(Bs7671Origin.Project, t.Origin);
            Assert.Equal("4D2A", t.ReplacedCorporateId);
            Assert.Contains("project table MFR-C", t.Cite());
            Assert.Contains("replaces corporate Table 4D2A", t.Cite());
            Assert.Contains(d.ProjectChanges, c => c.Contains("replaced Table 4D2A"));
            // The other 4D2A methods are untouched.
            Assert.Equal(Bs7671Origin.Corporate, d.FindTable("Cu", "PVC70", "B").Origin);
        }

        [Fact]
        public void A_table_for_a_new_key_is_added()
        {
            int before = Layer(null).Tables.Count;
            var d = Layer(Override(Tables(McTable(method: "F", id: "MFR-F"))));
            Assert.True(string.IsNullOrEmpty(d.LoadError), d.LoadError);
            Assert.Equal(before + 1, d.Tables.Count);
            Assert.Equal("MFR-F", d.FindTable("Cu", "PVC70", "F", "Multicore").Id);
            Assert.Contains(d.ProjectChanges, c => c.StartsWith("added project table MFR-F"));
        }

        [Fact]
        public void Sizing_on_a_project_table_says_so_in_the_basis()
        {
            var r = Bs7671CableSizer.Size(Pvc(20), Layer(Override(Tables(McTable()))));
            Assert.True(r.Sized, r.Refusal);
            Assert.True(r.ProjectTable);
            Assert.Contains("project table MFR-C", r.Basis);
            Assert.Contains("Sized on PROJECT data from _BIM_COORD/bs7671_wire_tables.json", r.Basis);
        }

        // ── two-source attestation ──────────────────────────────────────────

        [Fact]
        public void Project_rows_claiming_verified_without_attestation_are_downgraded_with_a_warning()
        {
            var d = Layer(Override(Tables(McTable(verified: true))));
            var t = d.FindTable("Cu", "PVC70", "C");
            Assert.All(t.Rows, r => { Assert.False(r.Verified); Assert.False(r.MvVerified); });
            Assert.Contains(d.Warnings, w => w.Contains("MFR-C") && w.Contains("no twoSourceCheck"));
            var s = Bs7671CableSizer.Size(Pvc(20), d);
            Assert.True(s.UnverifiedRow);
            Assert.Contains("VERIFY", s.Basis);
        }

        [Fact]
        public void A_valid_attestation_keeps_rows_verified()
        {
            var check = new JObject
            {
                ["sources"] = new JArray("Maker X datasheet rev 4", "BS 7671:2018+A2 Table 4D2A"),
                ["checkedBy"] = "A. Engineer", ["date"] = "2026-09-01",
            };
            var d = Layer(Override(Tables(McTable(verified: true, check: check))));
            var t = d.FindTable("Cu", "PVC70", "C");
            Assert.All(t.Rows, r => Assert.True(r.Verified));
            Assert.Empty(d.Warnings);
            Assert.Contains("A. Engineer", t.Cite());
        }

        [Fact]
        public void An_attestation_with_one_distinct_source_is_not_enough()
        {
            var check = new JObject
            {
                ["sources"] = new JArray("Maker X datasheet", "maker x DATASHEET"),
                ["checkedBy"] = "A. Engineer", ["date"] = "2026-09-01",
            };
            var d = Layer(Override(Tables(McTable(verified: true, check: check))));
            Assert.All(d.FindTable("Cu", "PVC70", "C").Rows, r => Assert.False(r.Verified));
            Assert.Contains(d.Warnings, w => w.Contains("fewer than two distinct sources"));
        }

        // ── removal ─────────────────────────────────────────────────────────

        [Fact]
        public void A_removed_table_is_refused_by_name_not_substituted()
        {
            var sec = new JObject
            {
                ["removeTables"] = new JArray(new JObject
                    { ["conductor"] = "Cu", ["insulation"] = "PVC70", ["cableType"] = "Multicore", ["installMethod"] = "C" }),
            };
            var d = Layer(Override(sec));
            Assert.True(string.IsNullOrEmpty(d.LoadError), d.LoadError);
            Assert.Null(d.FindTable("Cu", "PVC70", "C"));
            var r = Bs7671CableSizer.Size(Pvc(20), d);
            Assert.False(r.Sized);
            Assert.Contains("removes the table", r.Refusal);
            Assert.Contains("bs7671_wire_tables.json", r.Refusal);
        }

        [Fact]
        public void Removing_a_table_that_does_not_exist_is_an_error()
        {
            var sec = new JObject
            {
                ["removeTables"] = new JArray(new JObject
                    { ["conductor"] = "Cu", ["insulation"] = "PVC70", ["cableType"] = "Multicore", ["installMethod"] = "G" }),
            };
            Assert.Contains("removeTables names no corporate table", Layer(Override(sec)).LoadError);
        }

        [Fact]
        public void Removing_and_supplying_the_same_key_is_an_error()
        {
            var sec = Tables(McTable());
            sec["removeTables"] = new JArray(new JObject
                { ["conductor"] = "Cu", ["insulation"] = "PVC70", ["cableType"] = "Multicore", ["installMethod"] = "C" });
            Assert.Contains("both removed and supplied", Layer(Override(sec)).LoadError);
        }

        // ── atomic refusal ──────────────────────────────────────────────────

        [Fact]
        public void An_invalid_override_leaves_no_tables_and_every_sizer_refuses()
        {
            var bad = Override(Tables(McTable()));
            bad["schema"] = "wrong";
            var d = Layer(bad);
            Assert.False(string.IsNullOrEmpty(d.LoadError));
            Assert.Empty(d.Tables);
            Assert.Contains("Corporate tables were NOT used as a substitute", d.LoadError);
            var r = Bs7671CableSizer.Size(Pvc(20), d);
            Assert.False(r.Sized);
            Assert.Equal(d.LoadError, r.Refusal);
            Assert.StartsWith("NOT IN FORCE", Bs7671TableLayering.Describe(d));
        }

        [Fact]
        public void An_override_that_is_not_json_is_refused()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{ not json");
            try
            {
                var d = Bs7671TableLayering.LoadLayered(CorporatePath(), path);
                Assert.Contains("not valid JSON", d.LoadError);
                Assert.Empty(d.Tables);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Unknown_keys_are_errors_not_ignored()
        {
            var o = Override(Tables(McTable()));
            o["wireSizes"] = new JObject();
            Assert.Contains("unknown key 'wireSizes'", Layer(o).LoadError);
        }

        [Fact]
        public void It_that_falls_with_size_is_an_error()
        {
            var e = Bs7671TableLayering.Validate(Override(Tables(McTable(it25: 15.0))), Bs7671Data.FromJson(Corporate()));
            Assert.Contains(e, x => x.Contains("It falls"));
        }

        [Fact]
        public void Two_tables_for_one_key_are_an_error()
        {
            var e = Bs7671TableLayering.Validate(Override(Tables(McTable(), McTable(id: "MFR-C2"))), Bs7671Data.FromJson(Corporate()));
            Assert.Contains(e, x => x.Contains("a second table"));
        }

        [Fact]
        public void A_table_with_no_source_anywhere_is_an_error()
        {
            var t = McTable();
            t.Remove("source");
            var e = Bs7671TableLayering.Validate(Override(Tables(t)), Bs7671Data.FromJson(Corporate()));
            Assert.Contains(e, x => x.Contains("no source"));
            // A file-level source is enough.
            Assert.Empty(Bs7671TableLayering.Validate(Override(Tables(t), source: "Maker X"), Bs7671Data.FromJson(Corporate())));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.2)]
        public void Cf_outside_zero_to_one_is_an_error(double cf)
        {
            var e = Bs7671TableLayering.Validate(Override(new JObject { ["semiEnclosedFuseFactorCf"] = cf }), Bs7671Data.FromJson(Corporate()));
            Assert.Contains(e, x => x.Contains("semiEnclosedFuseFactorCf"));
        }

        [Fact]
        public void Grouping_keys_must_be_circuit_counts()
        {
            var sec = new JObject { ["groupingFactors"] = new JObject { ["Bunched"] = new JObject { ["two"] = 0.8 } } };
            Assert.Contains(Bs7671TableLayering.Validate(Override(sec), Bs7671Data.FromJson(Corporate())),
                            x => x.Contains("is not a number"));
        }

        // ── factors ─────────────────────────────────────────────────────────

        [Fact]
        public void A_project_ambient_factor_is_used_and_flagged()
        {
            var sec = new JObject
            {
                ["ambientTemperatureFactors"] = new JObject
                    { ["PVC70"] = new JObject { ["30"] = 1.0, ["35"] = 0.5 } },
            };
            var d = Layer(Override(sec, source: "Site survey"));
            Assert.True(string.IsNullOrEmpty(d.LoadError), d.LoadError);
            var r = Bs7671CableSizer.Size(Pvc(10, ambient: 35), d);
            Assert.True(r.Sized, r.Refusal);
            Assert.Equal(0.5, r.Ca, 3);
            Assert.True(r.ProjectFactors);
            Assert.Contains("[project override]", r.Basis);
            Assert.Contains("Sized on PROJECT data", r.Basis);
        }

        // ── voltage drop citation ───────────────────────────────────────────

        [Fact]
        public void Voltage_drop_from_a_project_table_cites_the_project_table()
        {
            var t = Layer(Override(Tables(McTable()))).FindTable("Cu", "PVC70", "C");
            var vd = VoltageDropSolver.Solve(new VoltageDropQuery
                { CsaMm2 = 2.5, LoadAmps = 10, LengthM = 20, NominalVoltageV = 230, Material = "Cu" }, t);
            Assert.True(vd.Computed, vd.Refusal);
            Assert.Contains("project table MFR-C-VD", vd.Basis);

            var corp = VoltageDropSolver.Solve(new VoltageDropQuery
                { CsaMm2 = 2.5, LoadAmps = 10, LengthM = 20, NominalVoltageV = 230, Material = "Cu" },
                Layer(null).FindTable("Cu", "PVC70", "C"));
            Assert.Contains("Table 4D2B", corp.Basis);
            Assert.DoesNotContain("project", corp.Basis);
        }
    }
}
