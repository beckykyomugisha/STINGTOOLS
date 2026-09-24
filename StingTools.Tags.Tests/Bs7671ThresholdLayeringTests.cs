// ══════════════════════════════════════════════════════════════════════════
//  Bs7671ThresholdLayeringTests.cs — corporate STING_BS7671_DISCONNECTION.json
//  + project _BIM_COORD/bs7671_disconnection.json (ROADMAP ELEC-14).
//
//  Ze was corporate-only and UK-valued, and Cmin was a C# constant beside a
//  JSON-driven U0. A Ugandan (UMEME) project had no way to declare its own Ze.
//
//  RED / GREEN, recorded 2026-09-24:
//    shipped file without "cMin"        RED   (key absent: Cmin was a C# const)
//    project Ze override                n/a   (no override layer existed to test)
//    with this change                   GREEN
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.Compliance;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Bs7671ThresholdLayeringTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting_bs7671_" + Guid.NewGuid().ToString("N"));

        public Bs7671ThresholdLayeringTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private static string ShippedPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data", "STING_BS7671_DISCONNECTION.json");
        }

        private string Write(string name, string json)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, json);
            return p;
        }

        [Fact]
        public void Shipped_file_declares_Cmin_095_and_loads_without_warnings()
        {
            // The key itself must be present - a missing key would silently fall back
            // to DefaultCmin and this test would pass against a file that says nothing.
            Assert.NotNull(JObject.Parse(File.ReadAllText(ShippedPath()))["cMin"]);
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), null);
            Assert.Empty(t.Warnings);
            Assert.Equal(0.95, t.Cmin, 6);
            Assert.Equal(230, t.NominalUo, 6);
            Assert.Equal("corporate", t.ZeSource["TN-C-S"]);
        }

        // BS 7671 Table 41.3, Type B 32 A: max Zs = Cmin × U0 / Ia = 0.95 × 230 / (5 × 32)
        //   = 218.5 / 160 = 1.3656 ohm, tabulated as 1.37 ohm.
        [Fact]
        public void Shipped_data_reproduces_Table_41_3_B32()
        {
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), null);
            double zsMax = t.Cmin * t.NominalUo / (t.IaMultiplier["MCB_B"] * 32);
            Assert.Equal(1.37, zsMax, 2);
        }

        // Project declares TN-C-S Ze = 0.25 ohm; TN-S is not mentioned so stays corporate 0.80.
        [Fact]
        public void Project_override_wins_per_key_and_leaves_the_rest()
        {
            string over = Write("bs7671_disconnection.json",
                "{ \"earthingSystems\": { \"TN-C-S\": { \"ZeOhm\": 0.25 } } }");
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), over);

            Assert.Equal(0.25, t.Ze["TN-C-S"], 6);
            Assert.Equal("project", t.ZeSource["TN-C-S"]);
            Assert.Equal(0.80, t.Ze["TN-S"], 6);
            Assert.Equal("corporate", t.ZeSource["TN-S"]);
            Assert.Equal(0.95, t.Cmin, 6);
            Assert.Equal(new[] { "corporate", "project" }, t.Sources);
        }

        [Fact]
        public void Project_can_change_Cmin_and_U0()
        {
            string over = Write("o.json", "{ \"cMin\": 0.90, \"nominalUoV\": 240 }");
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), over);
            Assert.Equal(0.90, t.Cmin, 6);
            Assert.Equal(240, t.NominalUo, 6);
        }

        [Fact]
        public void Out_of_range_or_mistyped_values_are_rejected_loudly()
        {
            string over = Write("bad.json",
                "{ \"cMin\": 1.5, \"earthingSystems\": { \"TT\": { \"ZeOhm\": \"high\" } } }");
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), over);
            Assert.Equal(0.95, t.Cmin, 6);          // kept
            Assert.Equal(21.0, t.Ze["TT"], 6);      // kept
            Assert.Equal(2, t.Warnings.Count);
        }

        [Fact]
        public void Missing_override_file_is_not_an_error()
        {
            var t = BS7671Thresholds.LoadLayered(ShippedPath(), Path.Combine(_dir, "absent.json"));
            Assert.Empty(t.Warnings);
            Assert.Equal(new[] { "corporate" }, t.Sources);
        }
    }
}
