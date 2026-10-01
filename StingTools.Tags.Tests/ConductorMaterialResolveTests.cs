using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core.Electrical;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Where a command's conductor material comes from: recorded on the element, else the
    /// caller's setting, else copper ASSUMED and said so. Before, the circuit wizard, the
    /// feeder sizer and the report commands passed a literal "Cu" and nothing said so.
    /// </summary>
    public class ConductorMaterialResolveTests
    {
        [Fact]
        public void Recorded_material_wins_over_the_setting()
        {
            var r = ConductorMaterialText.Resolve("Aluminium", "Cu");
            Assert.True(r.Ok);
            Assert.Equal(ConductorMaterial.Aluminum, r.Material);
            Assert.False(r.Assumed);
            Assert.Contains("recorded", r.Basis);
            Assert.Equal("Al", r.Label);
        }

        [Fact]
        public void The_setting_is_used_when_nothing_is_recorded()
        {
            var r = ConductorMaterialText.Resolve("  ", "CCA");
            Assert.True(r.Ok);
            Assert.Equal(ConductorMaterial.CopperCladAluminum, r.Material);
            Assert.False(r.Assumed);
        }

        [Fact]
        public void Nothing_recorded_or_set_is_copper_assumed_and_says_so()
        {
            var r = ConductorMaterialText.Resolve(null, null);
            Assert.True(r.Ok);
            Assert.Equal(ConductorMaterial.Copper, r.Material);
            Assert.True(r.Assumed);
            Assert.Contains("copper assumed", r.Basis);
        }

        [Theory]
        [InlineData("steel", null)]
        [InlineData(null, "Cu/Al")]
        public void Unrecognised_text_is_refused_not_copper(string recorded, string setting)
        {
            var r = ConductorMaterialText.Resolve(recorded, setting);
            Assert.False(r.Ok);
            Assert.Null(r.Label);
            Assert.Contains("not recognised", r.Refusal);
        }

        [Fact]
        public void IsAluminium_is_not_true_for_CCA()
        {
            Assert.True(ConductorMaterialText.IsAluminium("ALUMINIUM"));
            Assert.True(ConductorMaterialText.IsAluminium("al"));
            Assert.False(ConductorMaterialText.IsAluminium("CCA"));
            Assert.False(ConductorMaterialText.IsAluminium("Cu"));
        }

        // ── the voltage-drop resolver carries the material basis ───────────────

        private static Bs7671Data Data()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return Bs7671Data.FromJson(JObject.Parse(File.ReadAllText(
                Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json"))));
        }

        private static CircuitVdInput Circuit() => new CircuitVdInput
        {
            CurrentA = 20, LengthM = 25, VoltageV = 230, Phases = 1, CsaMm2 = 2.5, Material = "Cu", Standard = "BS7671",
            Insulation = "PVC70", InstallMethod = "C", CableType = "Multicore",
        };

        [Fact]
        public void An_assumed_material_is_named_in_the_drop_basis()
        {
            var c = Circuit();
            c.MaterialNote = ConductorMaterialText.CopperAssumedNote;
            var r = CircuitVoltageDrop.Resolve(c, Data());
            Assert.True(r.HasValue);
            Assert.Contains("copper assumed", r.Detail);
        }

        [Fact]
        public void A_refused_material_gives_no_drop()
        {
            var c = Circuit();
            c.MaterialRefusal = "conductor material \"steel\" in ELC_WIRE_COND_MAT_TXT is not recognised (Cu, Al or CCA)";
            var r = CircuitVoltageDrop.Resolve(c, Data(), _ => 1.0);
            Assert.Equal(VdMethod.None, r.Method);
            Assert.Contains("steel", r.Detail);
        }

        [Fact]
        public void Upper_case_aluminium_is_aluminium_in_the_resistance_lookups()
        {
            var ws = WireTableSet.FromJson(JObject.Parse(File.ReadAllText(Path.Combine(
                FindData(), "STING_WIRE_TABLES.json"))));
            Assert.Equal(ws.GetMohmPerMetre(16, "Al"), ws.GetMohmPerMetre(16, "ALUMINIUM"), 9);
            Assert.True(ws.GetMohmPerMetre(16, "ALUMINIUM") > ws.GetMohmPerMetre(16, "Cu"));
        }

        [Fact]
        public void Unrecognised_material_text_gets_no_resistance_not_copper()
        {
            var ws = WireTableSet.FromJson(JObject.Parse(File.ReadAllText(Path.Combine(
                FindData(), "STING_WIRE_TABLES.json"))));
            Assert.Equal(0.0, ws.GetMohmPerMetre(16, "steel"));
            Assert.True(ws.GetMohmPerMetre(16, "copper") > 0);
        }

        private static string FindData()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }
    }
}
