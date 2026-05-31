using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StingTools.UI;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-25 — locks the RIBA 2030 / LETI / RICS WLCA fossil+biogenic carbon split.
    // (a) Data integrity on the shipped BLE_MATERIALS.csv:
    //     net == fossil + biogenic everywhere; fossil >= 0; biogenic <= 0;
    //     non-timber biogenic == 0 and fossil == net (no regression).
    // (b) The pure MaterialLookupParser maps the long-format fossil/biogenic props.
    public class TimberCarbonSplitTests
    {
        private static string Csv() => Path.Combine(AppContext.BaseDirectory, "Data", "BLE_MATERIALS.csv");

        private static double? Num(string s)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

        private sealed class Row { public string Name; public double? Net, Fossil, Bio; }

        private static List<Row> LoadBle()
        {
            var lines = File.ReadAllLines(Csv()).Where(l => !l.StartsWith("#")).ToList();
            var rdr = MaterialLookupParserTestsCsv.Parse(lines);
            var h = rdr[0];
            int nm = Array.IndexOf(h, "MAT_NAME");
            int net = Array.IndexOf(h, "PROP_CARBON_KG_M3");
            int fo = Array.IndexOf(h, "PROP_CARBON_FOSSIL_KG_M3");
            int bi = Array.IndexOf(h, "PROP_CARBON_BIOGENIC_KG_M3");
            Assert.True(fo >= 0 && bi >= 0, "new fossil/biogenic columns must exist");
            var rows = new List<Row>();
            foreach (var f in rdr.Skip(1))
            {
                if (f.Length <= bi) continue;
                rows.Add(new Row { Name = f.Length > nm ? f[nm] : "", Net = Num(f[net]), Fossil = Num(f[fo]), Bio = Num(f[bi]) });
            }
            return rows;
        }

        [Fact]
        public void EveryRow_NetEqualsFossilPlusBiogenic()
        {
            foreach (var r in LoadBle().Where(r => r.Net.HasValue && r.Fossil.HasValue && r.Bio.HasValue))
                Assert.True(Math.Abs((r.Fossil.Value + r.Bio.Value) - r.Net.Value) <= 1.0,
                    $"{r.Name}: fossil({r.Fossil})+biogenic({r.Bio}) != net({r.Net})");
        }

        [Fact]
        public void Fossil_NonNegative_Biogenic_NonPositive()
        {
            foreach (var r in LoadBle())
            {
                if (r.Fossil.HasValue) Assert.True(r.Fossil.Value >= 0, $"{r.Name}: fossil < 0");
                if (r.Bio.HasValue) Assert.True(r.Bio.Value <= 0, $"{r.Name}: biogenic > 0");
            }
        }

        [Fact]
        public void NonTimber_BiogenicZero_AndFossilEqualsNet()
        {
            foreach (var r in LoadBle().Where(r => r.Bio == 0 && r.Net.HasValue && r.Fossil.HasValue))
                Assert.Equal(r.Net.Value, r.Fossil.Value, 3); // non-timber: net carried over unchanged
        }

        [Fact]
        public void TimberRows_HaveSequestration()
        {
            int timber = LoadBle().Count(r => r.Bio.HasValue && r.Bio.Value < 0);
            Assert.True(timber >= 41, $"expected >= 41 timber rows with biogenic < 0, got {timber}");
        }

        [Fact]
        public void Timber_SoftwoodDensity720_MatchesIceV3Split()
        {
            // ICE v3.0 sawn softwood: fossil 0.263, biogenic -1.64 kgCO2e/kg.
            // At rho=720: fossil 189, biogenic -1181, net -992.
            var r = LoadBle().FirstOrDefault(x => x.Bio == -1181 && x.Fossil == 189);
            Assert.NotNull(r);
            Assert.Equal(-992, r.Net.Value, 3);
        }

        [Fact]
        public void Parser_MapsLongFormatFossilAndBiogenic()
        {
            var c = MaterialLookupParser.Parse(new[]
            {
                "Category,TypeKey,Property,Value,Unit,Description",
                "TIMBER,SOFTWOOD,CARBON_FOSSIL_KG_M3,189,kgCO2/m3,fossil",
                "TIMBER,SOFTWOOD,CARBON_BIOGENIC_KG_M3,-1181,kgCO2/m3,biogenic",
                "TIMBER,SOFTWOOD,CARBON_KG_PER_M3,-992,kgCO2/m3,net",
            });
            var row = c["SOFTWOOD"];
            Assert.Equal(189, row.FossilCarbonKgCo2e);
            Assert.Equal(-1181, row.BiogenicCarbonKgCo2e);
            Assert.Equal(-992, row.CarbonKgCo2e);                       // net API unchanged
            Assert.Equal(row.CarbonKgCo2e, row.FossilCarbonKgCo2e + row.BiogenicCarbonKgCo2e, 3);
        }
    }

    // Local copy of the RFC-4180 splitter (FormulaSelfRefTests has a private one;
    // expose a shared one here for the BLE reader).
    internal static class MaterialLookupParserTestsCsv
    {
        public static List<string[]> Parse(IEnumerable<string> lines)
        {
            var outp = new List<string[]>();
            foreach (var line in lines)
            {
                var fields = new List<string>();
                var sb = new System.Text.StringBuilder();
                bool q = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (q)
                    {
                        if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                        else sb.Append(c);
                    }
                    else if (c == '"') q = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
                fields.Add(sb.ToString());
                outp.Add(fields.ToArray());
            }
            return outp;
        }
    }
}
