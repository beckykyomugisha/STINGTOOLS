using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core.Calc;
using StingTools.Core.Electrical;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Copper-clad aluminium (CCA) as a conductor material. NFPA 70-2023, as reproduced by
    /// NFPA in the 2026-cycle committee reports (base text = 2023 edition):
    /// <list type="bullet">
    /// <item>240.4(D)(3) 14 AWG CCA 10 A; (D)(5) 12 AWG Al and CCA 15 A; (D)(7) 10 AWG Al
    /// and CCA 25 A — PI 705-NFPA 70-2023, CMP-10 PI report pp. 321-322.</item>
    /// <item>Table 310.16 carries Al and CCA in one column group, "ALUMINUM OR COPPER-CLAD
    /// ALUMINUM", whose 14 AWG row is blank in 2023 (14 AWG CCA ampacities were only added by
    /// FR-8257 for the 2026 edition) — PI 1432-NFPA 70-2023, CMP-6 PI report p. 78/307.</item>
    /// </list>
    /// BS 7671 has no CCA data here, so every BS path must refuse CCA by name — never treat it
    /// as copper, which is what the old "anything not Al is Cu" tests did.
    /// </summary>
    public class CopperCladAluminiumTests
    {
        private static readonly int[] Nec =
        {
            10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200,
            225, 250, 300, 350, 400, 450, 500, 600, 700, 800, 1000, 1200, 1600, 2000, 2500,
            3000, 4000, 5000, 6000
        };

        private static JObject Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_WIRE_TABLES.json")));
        }

        // ── the material text ─────────────────────────────────────────────────

        [Theory]
        [InlineData("Cu", ConductorMaterial.Copper)]
        [InlineData("copper", ConductorMaterial.Copper)]
        [InlineData(null, ConductorMaterial.Copper)]
        [InlineData("", ConductorMaterial.Copper)]
        [InlineData("Al", ConductorMaterial.Aluminum)]
        [InlineData("aluminium", ConductorMaterial.Aluminum)]
        [InlineData("ALUMINUM", ConductorMaterial.Aluminum)]
        [InlineData("CCA", ConductorMaterial.CopperCladAluminum)]
        [InlineData("cca", ConductorMaterial.CopperCladAluminum)]
        [InlineData("Copper-clad aluminium", ConductorMaterial.CopperCladAluminum)]
        [InlineData("copper-clad aluminum", ConductorMaterial.CopperCladAluminum)]
        public void Material_text_parses(string text, ConductorMaterial expected)
        {
            Assert.True(ConductorMaterialText.TryParse(text, out var m));
            Assert.Equal(expected, m);
        }

        [Theory]
        [InlineData("steel")]
        [InlineData("Cu/Al")]
        public void Unknown_material_text_does_not_become_copper(string text)
            => Assert.False(ConductorMaterialText.TryParse(text, out _));

        [Fact]
        public void Labels()
        {
            Assert.Equal("Cu", ConductorMaterialText.Label(ConductorMaterial.Copper));
            Assert.Equal("Al", ConductorMaterialText.Label(ConductorMaterial.Aluminum));
            Assert.Equal("CCA", ConductorMaterialText.Label(ConductorMaterial.CopperCladAluminum));
            Assert.True(ConductorMaterialText.IsCopperClad("CCA"));
            Assert.True(ConductorMaterialText.IsCopperClad(" copper-clad aluminium "));
            Assert.False(ConductorMaterialText.IsCopperClad("Cu"));
            Assert.False(ConductorMaterialText.IsCopperClad("Al"));
        }

        // ── NEC 240.4(D) and Table 310.16 ─────────────────────────────────────

        [Theory]
        [InlineData("14", 10)]
        [InlineData("12", 15)]
        [InlineData("10", 25)]
        [InlineData("8", 0)]
        public void Small_conductor_limits_for_CCA_240_4D(string size, int expected)
            => Assert.Equal(expected, NECStandards.GetSmallConductorMaxOcpd(size, ConductorMaterial.CopperCladAluminum));

        [Fact]
        public void Aluminium_has_no_14_AWG_limit_only_CCA_does()
            => Assert.Equal(0, NECStandards.GetSmallConductorMaxOcpd("14", ConductorMaterial.Aluminum));

        [Theory]
        [InlineData("12", 75, 20)]
        [InlineData("10", 75, 30)]
        [InlineData("4/0", 75, 180)]
        [InlineData("500", 90, 350)]
        public void CCA_ampacity_is_the_aluminium_column_of_310_16(string size, int temp, int expected)
        {
            Assert.Equal(expected, NECStandards.GetConductorAmpacity(size, ConductorMaterial.CopperCladAluminum, temp));
            Assert.Equal(NECStandards.GetConductorAmpacity(size, ConductorMaterial.Aluminum, temp),
                         NECStandards.GetConductorAmpacity(size, ConductorMaterial.CopperCladAluminum, temp));
        }

        [Fact]
        public void CCA_14_AWG_has_no_2023_ampacity()
            => Assert.Throws<ArgumentException>(() => NECStandards.GetConductorAmpacity("14", ConductorMaterial.CopperCladAluminum, 75));

        [Fact]
        public void CCA_conductor_pick_starts_at_12_AWG()
        {
            var p = NecConductorSelection.Pick(10, false, ConductorMaterial.CopperCladAluminum, 30, 2, Nec);
            Assert.Equal("12", p.Size);
            Assert.Equal(10, p.Device.ProposedA);
        }

        [Fact]
        public void CCA_is_upsized_past_its_240_4D_limit_and_labelled_CCA()
        {
            // 18 A → 20 A device; 12 AWG CCA (20 A at 75 °C) is limited to 15 A → 10 AWG (limit 25 A).
            var p = NecConductorSelection.Pick(18, false, ConductorMaterial.CopperCladAluminum, 30, 2, Nec);
            Assert.Equal("10", p.Size);
            Assert.Equal(20, p.Device.ProposedA);
            Assert.Contains(p.UpsizedPast, s => s.StartsWith("12 AWG") && s.Contains("15 A"));
        }

        [Fact]
        public void A_14_AWG_CCA_device_above_10A_is_blocked_and_named()
        {
            var sel = new ProtectiveDeviceSelection.Selection { ProposedA = 15, Note = "" };
            Assert.True(NecConductorSelection.ApplySmallConductorLimit(sel, "14", ConductorMaterial.CopperCladAluminum));
            Assert.True(sel.Blocked);
            Assert.Contains("10 A for 14 AWG CCA", sel.Note);
        }

        // ── BS / IEC paths refuse CCA by name ─────────────────────────────────

        [Fact]
        public void BS_cable_sizer_refuses_CCA_by_name()
        {
            var r = Bs7671CableSizer.Size(new Bs7671SizingInput
            {
                DesignCurrentA = 20, VoltageV = 230, Phases = 1, LengthM = 10, InstallMethod = "C",
                Insulation = "PVC70", Material = "CCA", AmbientTempC = 30, VdLimitPct = 5,
                DeviceRatingsA = new[] { 6, 10, 16, 20, 25, 32 },
            }, Bs7671Data.FromJson(Root()));
            Assert.False(r.Sized);
            Assert.Contains("copper-clad aluminium", r.Refusal);
        }

        [Fact]
        public void BS_voltage_drop_solver_refuses_CCA()
        {
            var r = VoltageDropSolver.Solve(new VoltageDropQuery
            {
                CsaMm2 = 16, LoadAmps = 40, LengthM = 50, NominalVoltageV = 230, Material = "CCA"
            });
            Assert.Contains("copper-clad aluminium", r.Refusal);
            Assert.Equal(0.0, r.VoltDropPct);
        }

        [Theory]
        [InlineData("BS7671")]
        [InlineData("NEC2023")]
        public void Circuit_voltage_drop_is_NONE_for_CCA_under_either_standard(string std)
        {
            var c = new CircuitVdInput
            {
                CurrentA = 20, LengthM = 25, VoltageV = 230, Phases = 1, CsaMm2 = 2.5, Material = "CCA",
                Standard = std, Insulation = "PVC70", InstallMethod = "C", CableType = "Multicore",
            };
            var r = CircuitVoltageDrop.Resolve(c, Bs7671Data.FromJson(Root()), _ => 1.23);
            Assert.Equal(VdMethod.None, r.Method);
            Assert.Contains("copper-clad aluminium", r.Detail);
        }

        [Fact]
        public void Copper_resistance_is_not_used_for_CCA()
        {
            Assert.Equal(0.0, VoltageDropEngine.BaseResistanceMohmPerM(2.5, "CCA"));
            Assert.Equal(0.0, VoltageDropEngine.CalculateVoltDropPercent(20, 25, 2.5, "CCA", 230, 1));
            Assert.True(VoltageDropEngine.BaseResistanceMohmPerM(2.5, "Cu") > 0);
            var ws = WireTableSet.FromJson(Root());
            Assert.Equal(0.0, ws.GetMohmPerMetre(16, "CCA"));
            Assert.True(ws.GetMohmPerMetre(16, "Cu") > 0);
        }
    }
}
