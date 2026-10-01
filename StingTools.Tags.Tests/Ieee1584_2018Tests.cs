// ══════════════════════════════════════════════════════════════════════════
//  Ieee1584_2018Tests.cs — MEPG-11 / ELEC-1: the IEEE 1584-2018 arc-flash model.
//
//  Oracles, in order of strength:
//   1. IEEE 1584-2018 Annex D.1 (4.16 kV) and D.2 (0.48 kV) worked examples, every
//      intermediate value printed there.
//   2. 600 rows of the result set the official IEEE 1584-2018 spreadsheet produces
//      (TestData/ieee1584_2018_spreadsheet_sample.csv; LiaungYip/arcflash, MIT),
//      60 per electrode configuration at LV and at MV, full and reduced cases.
//   3. The reduced-case point Li-aung Yip reported to IEEE DataPort in 2022, where the
//      official spreadsheet V2.6.6 is wrong (VarCf not applied to the intermediate currents).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StingTools.Commands.Electrical.ArcFlash;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Ieee1584_2018Tests
    {
        private static void Near(double expected, double actual, double relTol, string what)
            => Assert.True(Math.Abs(actual / expected - 1) <= relTol, $"{what}: expected {expected}, got {actual}");

        // ── Annex D.1: VCB, 4.16 kV, 15 kA, G 104, D 914.4, 1143 × 762 × 508, T 197 / 223 ms ──

        [Fact]
        public void Annex_D1_medium_voltage_intermediate_values()
        {
            var c = ElectrodeConfiguration.VCB;
            Assert.Equal(11.117, Ieee1584_2018.IntermediateArcingCurrentKa(c, 0, 15, 104), 3);
            Assert.Equal(12.816, Ieee1584_2018.IntermediateArcingCurrentKa(c, 1, 15, 104), 3);
            Assert.Equal(14.116, Ieee1584_2018.IntermediateArcingCurrentKa(c, 2, 15, 104), 3);
            Assert.Equal(12.979, Ieee1584_2018.ArcingCurrentKa(c, 4.16, 15, 104, false), 3);
            Assert.Equal(0.047, Ieee1584_2018.VarCf(c, 4.16), 3);
            Assert.Equal(12.675, Ieee1584_2018.ArcingCurrentKa(c, 4.16, 15, 104, true), 3);
            double cf = Ieee1584_2018.EnclosureCorrection(c, 4.16, 1143, 762, 508, out string type, out double ees);
            Assert.Equal("Typical", type);
            Assert.Equal(36.316, ees, 3);
            Assert.Equal(1.284, cf, 3);
        }

        [Fact]
        public void Annex_D1_medium_voltage_energy_and_boundary()
        {
            var r = Ieee1584_2018.Calculate(ElectrodeConfiguration.VCB, 4.16, 15, 104, 914.4, 1143, 762, 508,
                ia => ia > 12.8 ? 197 : 223);
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.Equal(12.152, r.Full.IncidentEnergyJcm2, 3);
            Assert.Equal(1606, r.Full.BoundaryMm, 0);
            Assert.Equal(13.343, r.Reduced.IncidentEnergyJcm2, 3);
            Assert.Equal(1704, r.Reduced.BoundaryMm, 0);
            Assert.True(r.ReducedCaseGovernsEnergy);
            Assert.Equal(13.343, r.IncidentEnergyJcm2, 3);
            Assert.Equal(1704, r.BoundaryMm, 0);
        }

        // ── Annex D.2: VCB, 0.48 kV, 45 kA, G 32, D 609.6, 610 × 610 × 254, T 61.3 / 319 ms ──

        [Fact]
        public void Annex_D2_low_voltage()
        {
            var c = ElectrodeConfiguration.VCB;
            Assert.Equal(32.449, Ieee1584_2018.IntermediateArcingCurrentKa(c, 0, 45, 32), 3);
            Assert.Equal(0.247, Ieee1584_2018.VarCf(c, 0.48), 3);
            double cf = Ieee1584_2018.EnclosureCorrection(c, 0.48, 610, 610, 254, out _, out double ees);
            Assert.Equal(24.016, ees, 3);
            Assert.Equal(1.085, cf, 3);

            var r = Ieee1584_2018.Calculate(c, 0.48, 45, 32, 609.6, 610, 610, 254, ia => ia > 27 ? 61.3 : 319);
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.Equal(28.793, r.Full.ArcingCurrentKa, 3);
            Assert.Equal(11.585, r.Full.IncidentEnergyJcm2, 3);
            Assert.Equal(1029, r.Full.BoundaryMm, 0);
            Assert.Equal(25.244, r.Reduced.ArcingCurrentKa, 3);
            Assert.Equal(53.156, r.Reduced.IncidentEnergyJcm2, 3);
            Assert.Equal(2669, r.Reduced.BoundaryMm, 0);
        }

        [Fact]
        public void Reduced_case_just_above_600V_applies_VarCf_to_every_intermediate_current()
        {
            // The official spreadsheet V2.6.6 gives 7.0153 J/cm² here; the standard's equations give 4.7638.
            var r = Ieee1584_2018.Case(ElectrodeConfiguration.VCB, 0.601, 65, 19.05, 305,
                Ieee1584_2018.EnclosureCorrection(ElectrodeConfiguration.VCB, 0.601, 200, 200, 100, out _, out _), 10, true);
            Assert.Equal(4.7638, r.IncidentEnergyJcm2, 3);
        }

        // ── the official spreadsheet's results ─────────────────────────────

        public static IEnumerable<object[]> SpreadsheetRows()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.Tags.Tests", "TestData", "ieee1584_2018_spreadsheet_sample.csv")))
                dir = dir.Parent;
            if (dir == null) throw new FileNotFoundException("ieee1584_2018_spreadsheet_sample.csv not found above " + AppContext.BaseDirectory);
            var lines = File.ReadAllLines(Path.Combine(dir.FullName, "StingTools.Tags.Tests", "TestData", "ieee1584_2018_spreadsheet_sample.csv"))
                .Where(l => !l.StartsWith("#")).ToList();
            var head = lines[0].Split(',');
            foreach (var l in lines.Skip(1))
                yield return new object[] { string.Join(";", head.Zip(l.Split(','), (h, v) => h + "=" + v)) };
        }

        [Fact]
        public void Spreadsheet_sample_has_every_configuration_at_LV_and_MV()
        {
            var rows = SpreadsheetRows().Select(o => Parse((string)o[0])).ToList();
            Assert.Equal(600, rows.Count);
            foreach (var ec in Enum.GetValues(typeof(ElectrodeConfiguration)).Cast<ElectrodeConfiguration>())
            {
                Assert.Contains(rows, r => r.ec == ec && r.v <= 0.6);
                Assert.Contains(rows, r => r.ec == ec && r.v > 0.6);
            }
        }

        [Theory]
        [MemberData(nameof(SpreadsheetRows))]
        public void Matches_the_official_spreadsheet(string row)
        {
            var x = Parse(row);
            double cf = Ieee1584_2018.EnclosureCorrection(x.ec, x.v, x.h, x.w, x.d, out _, out _);
            var full = Ieee1584_2018.Case(x.ec, x.v, x.ibf, x.g, x.dist, cf, x.t, false);
            var red = Ieee1584_2018.Case(x.ec, x.v, x.ibf, x.g, x.dist, cf, x.t, true);
            const double tol = 1e-4;
            Near(x.f["I_arc_max"], full.ArcingCurrentKa, tol, "Iarc");
            Near(x.f["E_joules_max"], full.IncidentEnergyJcm2, tol, "E");
            Near(x.f["AFB_max"], full.BoundaryMm, tol, "AFB");
            Near(x.f["I_arc_min"], red.ArcingCurrentKa, tol, "Iarc,min");
            Near(x.f["E_joules_min"], red.IncidentEnergyJcm2, tol, "E,min");
            Near(x.f["AFB_min"], red.BoundaryMm, tol, "AFB,min");
        }

        private static (ElectrodeConfiguration ec, double v, double ibf, double g, double dist, double t, double w, double h, double d,
            Dictionary<string, double> f) Parse(string row)
        {
            var kv = row.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            var f = kv.Where(p => p.Key != "EC").ToDictionary(p => p.Key, p => double.Parse(p.Value, CultureInfo.InvariantCulture));
            var ec = (ElectrodeConfiguration)Enum.Parse(typeof(ElectrodeConfiguration), kv["EC"]);
            return (ec, f["V_oc"], f["I_bf"], f["G"], f["D"], f["T"], f["width"], f["height"], f["depth"], f);
        }

        // ── scope ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0.2, 20, 25, 455)]     // below 208 V
        [InlineData(16, 20, 104, 914)]     // above 15 kV
        [InlineData(0.4, 110, 25, 455)]    // above 106 kA at LV
        [InlineData(11, 70, 152, 914)]     // above 65 kA above 600 V
        [InlineData(11, 20, 13, 914)]      // 13 mm gap below the MV minimum
        [InlineData(0.4, 20, 25, 300)]     // working distance below 305 mm
        public void Outside_the_model_is_not_calculated(double kv, double ibf, double gap, double dist)
        {
            var r = Ieee1584_2018.Calculate(ElectrodeConfiguration.VCB, kv, ibf, gap, dist, 500, 500, 500, _ => 100);
            Assert.False(r.Calculated);
            Assert.False(string.IsNullOrWhiteSpace(r.NotCalculatedReason));
        }

        [Fact]
        public void Enclosure_narrower_than_four_gaps_is_not_calculated()
        {
            var r = Ieee1584_2018.Calculate(ElectrodeConfiguration.VCB, 0.4, 20, 50, 455, 355.6, 150, 300, _ => 100);
            Assert.False(r.Calculated);
            Assert.Contains("4 × the gap", r.NotCalculatedReason);
        }

        // ── through ArcFlashEngine ─────────────────────────────────────────

        [Fact]
        public void Engine_uses_board_geometry_when_given_and_assumes_nothing()
        {
            // The values ArcFlashCommand reads from ELC_ARC_FLASH_GAP_MM / _ENCL_H/W/D_MM.
            var r = ArcFlashEngine.Calculate(new ArcFlashInput
            {
                BoltedFaultKa = 25, VoltageV = 400, EquipmentClass = ArcEquipmentClass.PanelMcc, ClearingTimeS = 0.1,
                Electrode = ElectrodeConfiguration.VCB,
                GapMm = 32, EnclosureHeightMm = 508, EnclosureWidthMm = 508, EnclosureDepthMm = 250,
            });
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.Equal(32, r.GapMm);
            Assert.DoesNotContain(r.Notes, n => n.Contains("assumed"));
            Assert.DoesNotContain(r.Notes, n => n.Contains("depth not stated"));
            var x = Ieee1584_2018.Calculate(ElectrodeConfiguration.VCB, 0.4, 25, 32, 457.2, 508, 508, 250, _ => 100);
            Assert.Equal(x.IncidentEnergyJcm2 / 4.184, r.IncidentEnergyCalCm2, 9);
        }

        [Fact]
        public void Engine_defaults_to_2018_with_typical_LV_panel_values_and_says_what_it_assumed()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput
            {
                BoltedFaultKa = 25, VoltageV = 400, EquipmentClass = ArcEquipmentClass.PanelMcc, ClearingTimeS = 0.1
            });
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.StartsWith("IEEE 1584-2018", r.Basis);
            Assert.Equal("VCB", r.ElectrodeConfiguration);
            Assert.Equal(25, r.GapMm);
            Assert.Equal(457.2, r.WorkingDistanceMm);
            Assert.Contains(r.Notes, n => n.Contains("electrode configuration VCB assumed"));
            Assert.Contains(r.Notes, n => n.Contains("depth not stated"));
            Assert.Contains(r.Notes, n => n.Contains("gap 25 mm assumed"));

            // Same numbers as the model called directly with a deep typical enclosure.
            var x = Ieee1584_2018.Calculate(ElectrodeConfiguration.VCB, 0.4, 25, 25, 457.2, 355.6, 304.8, double.PositiveInfinity, _ => 100);
            Assert.Equal(x.IncidentEnergyJcm2 / 4.184, r.IncidentEnergyCalCm2, 9);
            Assert.Equal(x.BoundaryMm, r.BoundaryMm, 9);
            string label = ArcFlashEngine.FormatLabel("DB-1", 400, ArcEquipmentClass.PanelMcc, r, "fixed", ArcFlashPresentationTests.Shipped());
            Assert.Contains("IEEE 1584-2018", label);
            Assert.Contains("Electrodes: VCB", label);
        }

        [Fact]
        public void Engine_2018_covers_medium_voltage_switchgear()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput
            {
                BoltedFaultKa = 15, VoltageV = 11000, EquipmentClass = ArcEquipmentClass.Switchgear, ClearingTimeS = 0.2
            });
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.Equal(152, r.GapMm);
            Assert.Equal(914.4, r.WorkingDistanceMm);
        }

        [Fact]
        public void Engine_2018_evaluates_the_clearing_time_at_both_arcing_currents()
        {
            var seen = new List<double>();
            var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400 },
                ia => { seen.Add(ia); return ia > 15 ? 0.05 : 0.5; });
            Assert.True(r.Calculated, r.NotCalculatedReason);
            Assert.Equal(2, seen.Count);
            Assert.True(r.ReducedArcingCurrentKa < r.ArcingCurrentKa);
            Assert.Equal(r.ReducedCaseGoverns ? r.ReducedClearingTimeS : r.ClearingTimeS, r.GoverningClearingTimeS);
        }

        [Fact]
        public void Engine_2018_unknown_clearing_time_is_not_calculated()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput { BoltedFaultKa = 25, VoltageV = 400 }, _ => double.NaN);
            Assert.False(r.Calculated);
            Assert.Equal(0, r.IncidentEnergyCalCm2);
            Assert.Contains("NOT CALCULATED", ArcFlashEngine.FormatLabel("DB", 400, ArcEquipmentClass.PanelMcc, r, "", ArcFlashPresentationTests.Shipped()));
        }

        [Fact]
        public void Open_air_has_no_typical_gap_and_is_refused_until_one_is_given()
        {
            var r = ArcFlashEngine.Calculate(new ArcFlashInput
            { BoltedFaultKa = 25, VoltageV = 400, EquipmentClass = ArcEquipmentClass.OpenAir, ClearingTimeS = 0.1 });
            Assert.False(r.Calculated);
            var ok = ArcFlashEngine.Calculate(new ArcFlashInput
            { BoltedFaultKa = 25, VoltageV = 400, EquipmentClass = ArcEquipmentClass.OpenAir, ClearingTimeS = 0.1, GapMm = 25, WorkingDistanceMm = 455 });
            Assert.True(ok.Calculated, ok.NotCalculatedReason);
            Assert.Equal("VOA", ok.ElectrodeConfiguration);
            Assert.Equal(1.0, ok.EnclosureCf);
        }
    }
}
