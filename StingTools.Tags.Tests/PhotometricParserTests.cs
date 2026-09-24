using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StingTools.Photometrics;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ELEC-11 — photometric parsing. The defects were: absolute (LED) IES files
    /// reported 0 lm, and EULUMDAT multiplied a set's TOTAL flux by its lamp count,
    /// read a full set of C-planes for symmetric files, and left cd/klm unconverted.
    /// </summary>
    public class PhotometricParserTests
    {
        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        private static List<double> Range(double from, double to, double step)
        {
            var l = new List<double>();
            for (double a = from; a <= to + 1e-9; a += step) l.Add(a);
            return l;
        }

        // ── Integrator ─────────────────────────────────────────────────────

        [Fact]
        public void IsotropicSourceIntegratesToFourPiI_Rotational()
        {
            var v = Range(0, 180, 5);
            var grid = new List<List<double>> { v.Select(_ => 100.0).ToList() };
            double phi = PhotometricIntegrator.ZonalLumens(v, new List<double> { 0 }, grid);
            Assert.Equal(4 * Math.PI * 100, phi, 6);
        }

        [Theory]
        [InlineData(0, 90, 15)]    // quadrant symmetry → ×4
        [InlineData(0, 180, 30)]   // bilateral → ×2
        [InlineData(0, 360, 45)]   // full circle
        [InlineData(0, 345, 15)]   // full circle without the duplicate 360° plane
        [InlineData(-90, 90, 30)]  // C270..C90 stored as -90..90 → ×2
        public void IsotropicSourceIntegratesToFourPiI_AnySymmetry(double h0, double h1, double dh)
        {
            var v = Range(0, 180, 10);
            var h = Range(h0, h1, dh);
            var grid = h.Select(_ => v.Select(__ => 250.0).ToList()).ToList();
            double phi = PhotometricIntegrator.ZonalLumens(v, h, grid);
            Assert.Equal(4 * Math.PI * 250, phi, 6);
        }

        [Fact]
        public void LambertianDownlightIntegratesToPiI0()
        {
            // I(γ) = I0·cos γ over the lower hemisphere → Φ = π·I0.
            var v = Range(0, 90, 0.5);
            var grid = new List<List<double>> { v.Select(g => 1000 * Math.Cos(g * Math.PI / 180)).ToList() };
            double phi = PhotometricIntegrator.ZonalLumens(v, new List<double> { 0 }, grid);
            Assert.Equal(Math.PI * 1000, phi, 0); // within 0.5 lm on 3142
        }

        [Fact]
        public void MalformedGridIsZeroNotAGuess()
        {
            Assert.Equal(0, PhotometricIntegrator.ZonalLumens(new List<double> { 0 }, new List<double> { 0 },
                new List<List<double>> { new List<double> { 5 } }));
            Assert.Equal(0, PhotometricIntegrator.ZonalLumens(null, null, null));
        }

        // ── IES ────────────────────────────────────────────────────────────

        private static string Ies(double lumensPerLamp, int lamps, double[] h, double cd, string tilt = "NONE")
        {
            var v = Range(0, 180, 10);
            var sb = new StringBuilder();
            sb.AppendLine("IESNA:LM-63-2002");
            sb.AppendLine("[MANUFAC] Synthetic");
            sb.AppendLine("TILT=" + tilt);
            if (tilt == "INCLUDE")
            {
                sb.AppendLine("1");
                sb.AppendLine("3");
                sb.AppendLine("0 45 90");        // several values on one line
                sb.AppendLine("1.0 0.95 0.9");
            }
            sb.AppendLine($"{lamps} {F(lumensPerLamp)} 1 {v.Count} {h.Length} 1 2 0.6 0.6 0.1");
            sb.AppendLine("1 1 42");
            sb.AppendLine(string.Join(" ", v.Select(F)));
            sb.AppendLine(string.Join(" ", h.Select(F)));
            foreach (var _ in h) sb.AppendLine(string.Join(" ", v.Select(__ => F(cd))));
            return sb.ToString();
        }

        [Fact]
        public void IesAbsolutePhotometryReportsIntegratedLumensNotZero()
        {
            var p = IesParser.Parse(Ies(-1, 1, new[] { 0.0 }, 100));
            Assert.True(p.AbsolutePhotometry);
            Assert.Equal(4 * Math.PI * 100, p.TotalLumens, 6);
            Assert.Equal(p.TotalLumens, p.LuminaireLumens, 9);
            Assert.Equal(42, p.TotalWatts);
        }

        [Fact]
        public void IesRelativePhotometryKeepsRatedLampFluxAndIntegratesLuminaireFlux()
        {
            var p = IesParser.Parse(Ies(1500, 2, new[] { 0.0, 90.0 }, 100));
            Assert.False(p.AbsolutePhotometry);
            Assert.Equal(3000, p.TotalLumens, 9);                 // 2 × 1500 lamp lumens
            Assert.Equal(4 * Math.PI * 100, p.LuminaireLumens, 6); // what the grid emits
        }

        [Fact]
        public void IesTiltIncludeWithValuesOnOneLineStillParsesTheGrid()
        {
            var p = IesParser.Parse(Ies(-1, 1, new[] { 0.0 }, 100, tilt: "INCLUDE"));
            Assert.Empty(p.Warnings);
            Assert.Equal(19, p.VerticalAngles.Count);
            Assert.Equal(4 * Math.PI * 100, p.TotalLumens, 6);
        }

        // ── EULUMDAT ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal EULUMDAT file. <paramref name="cdPerKlm"/> is the
        /// intensity in cd/1000 lm; every stored plane gets the same value.
        /// </summary>
        private static string Ldt(int isym, int mc, int lampCount, double setFlux, double setWatts,
                                  double cdPerKlm, double lorPct = 100)
        {
            var g = Range(0, 180, 10);
            int stored = isym switch { 1 => 1, 2 => mc / 2 + 1, 3 => mc / 2 + 1, 4 => mc / 4 + 1, _ => mc };
            var l = new List<string>
            {
                "Synthetic Co",            // 1
                "1",                       // 2 Ityp
                isym.ToString(),           // 3 Isym
                mc.ToString(),             // 4 Mc
                F(360.0 / mc),             // 5 Dc
                g.Count.ToString(),        // 6 Ng
                "10",                      // 7 Dg
                "RPT-1",                   // 8
                "Test luminaire",          // 9
                "CAT-1",                   // 10
                "test.ldt",                // 11
                "2026-09-24",              // 12
                "600", "600", "80",        // 13-15
                "580", "580", "0", "0", "0", "0", // 16-21
                "100",                     // 22 DFF
                F(lorPct),                 // 23 LORL
                "1",                       // 24 CFLI
                "0",                       // 25 tilt
                "1",                       // 26 n sets
                lampCount.ToString(),      // 26a
                "LED",                     // 26b
                F(setFlux),                // 26c total flux of the set
                "4000K",                   // 26d
                "80",                      // 26e
                F(setWatts),               // 26f
            };
            for (int i = 0; i < 10; i++) l.Add("0.5");                    // direct ratios
            for (int i = 0; i < mc; i++) l.Add(F(i * 360.0 / mc));         // C angles
            l.AddRange(g.Select(F));                                       // γ angles
            for (int c = 0; c < stored; c++) l.AddRange(g.Select(_ => F(cdPerKlm)));
            return string.Join("\n", l) + "\n";
        }

        [Fact]
        public void LdtSetFluxIsNotMultipliedByLampCount()
        {
            // 26c is the TOTAL flux of the set (2 lamps × 1000 lm = 2000 lm).
            var p = LdtParser.Parse(Ldt(isym: 1, mc: 4, lampCount: 2, setFlux: 2000, setWatts: 30,
                                        cdPerKlm: 1000 / (4 * Math.PI)));
            Assert.Equal(2000, p.TotalLumens, 9);
            Assert.Equal(30, p.TotalWatts, 9);
            Assert.Equal(2, p.LampCount);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]  // C0..C180 of 4 planes
        [InlineData(3, 3)]  // C270..C90 through C0
        [InlineData(4, 2)]  // C0..C90
        [InlineData(0, 4)]  // no symmetry — all planes
        public void LdtStoresOnlyThePlanesTheSymmetryIndicatorSays(int isym, int expectedPlanes)
        {
            var p = LdtParser.Parse(Ldt(isym, mc: 4, lampCount: 1, setFlux: 2000, setWatts: 20,
                                        cdPerKlm: 1000 / (4 * Math.PI)));
            Assert.Equal(expectedPlanes, p.Candela.Count);
            Assert.Equal(expectedPlanes, p.HorizontalAngles.Count);
            Assert.All(p.Candela, row => Assert.Equal(19, row.Count));
            // cd/klm → cd with the set flux: 1000/4π cd/klm × 2 klm; integrates to the lamp flux.
            Assert.Equal(2000 / (4 * Math.PI), p.PeakCandela, 3); // file carries 6 d.p.
            Assert.Equal(2000, p.LuminaireLumens, 2);
            Assert.DoesNotContain(p.Warnings, w => w.Contains("differs"));
        }

        [Fact]
        public void LdtIsym3PlanesAreMonotonicAroundC0()
        {
            var p = LdtParser.Parse(Ldt(3, mc: 4, lampCount: 1, setFlux: 1000, setWatts: 10, cdPerKlm: 50));
            Assert.Equal(new[] { -90.0, 0.0, 90.0 }, p.HorizontalAngles);
        }

        [Fact]
        public void LdtFlagsIntegratedFluxThatContradictsLampFluxTimesLor()
        {
            // Grid integrates to 100 % of lamp flux, but the file claims LOR 50 %.
            var p = LdtParser.Parse(Ldt(1, mc: 4, lampCount: 1, setFlux: 2000, setWatts: 20,
                                        cdPerKlm: 1000 / (4 * Math.PI), lorPct: 50));
            Assert.Contains(p.Warnings, w => w.Contains("differs"));
        }

        [Fact]
        public void LdtNegativeLampCountMarksAbsolutePhotometry()
        {
            var p = LdtParser.Parse(Ldt(1, mc: 4, lampCount: -1, setFlux: 3200, setWatts: 28, cdPerKlm: 80));
            Assert.True(p.AbsolutePhotometry);
            Assert.Equal(3200, p.TotalLumens, 9);
            Assert.Equal(1, p.LampCount);
        }
    }
}
