using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.ExLink;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>KUT deep review loop 2 — Fohlio findings L2-FOH-1 … 7.</summary>
    public class FohlioLoop2Tests
    {
        private static FohlioCandidate C(long id, string key, string fref) => new FohlioCandidate { Id = id, Key = key, FohlioRef = fref };
        private static FohlioRowIdentity R(int i, string key, string fref) => new FohlioRowIdentity { RowIndex = i, Key = key, FohlioRef = fref };

        private static string Src(params string[] rel)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(rel))).Replace("\r\n", "\n");
        }

        [Fact]
        public void L2Foh2_AnItemRefOnManyInstancesNarrowsByKey()
        {
            // 3 chairs share the item ref CH-01 after the first import; each row has its own tag.
            var els = new[] { C(1, "CH-T1", "CH-01"), C(2, "CH-T2", "CH-01"), C(3, "CH-T3", "CH-01") };
            var rows = new[] { R(0, "CH-T1", "CH-01"), R(1, "CH-T2", "CH-01"), R(2, "CH-T3", "CH-01") };
            var plan = FohlioImportPlanner.Match(rows, els, useFohlioRef: true);
            Assert.Equal(3, plan.Matches.Count);
            Assert.Empty(plan.Unmatched);
            Assert.Equal(new long[] { 1, 2, 3 }, plan.Matches.OrderBy(m => m.RowIndex).Select(m => m.CandidateId).ToArray());
        }

        [Fact]
        public void L2Foh2_ASharedRefWithNoUsableKeyIsStillRefused()
        {
            var els = new[] { C(1, "", "CH-01"), C(2, "", "CH-01") };
            var plan = FohlioImportPlanner.Match(new[] { R(0, "", "CH-01") }, els, useFohlioRef: true);
            Assert.Empty(plan.Matches);
            Assert.Single(plan.Unmatched);
        }

        [Theory]
        [InlineData("€ 1.250")]
        [InlineData("UGX 450.000")]
        public void L2Foh5_OneDotAndThreeDigitsIsAmbiguousAndRefused(string raw) =>
            Assert.False(FohlioMoney.TryParseCost(raw, out _, out _), raw);

        [Fact]
        public void L2Foh4_APriceEditedAfterImportIsStale()
        {
            var snap = new Dictionary<string, string> { ["ASS_MANUFACTURER_TXT"] = "Acme" };
            var ps = new[] { "ASS_MANUFACTURER_TXT", "FOHLIO_UNIT_COST_NR", "FOHLIO_CURRENCY_TXT" };
            Func<string, string> held = p => p == "ASS_MANUFACTURER_TXT" ? "Acme" : "";
            Assert.False(FohlioStale.IsStale(ps, held, 1250, "USD", snap, 1250, "USD", "FOHLIO_UNIT_COST_NR", "FOHLIO_CURRENCY_TXT"));
            Assert.True(FohlioStale.IsStale(ps, held, 900, "USD", snap, 1250, "USD", "FOHLIO_UNIT_COST_NR", "FOHLIO_CURRENCY_TXT"));
            Assert.True(FohlioStale.IsStale(ps, held, 1250, "UGX", snap, 1250, "USD", "FOHLIO_UNIT_COST_NR", "FOHLIO_CURRENCY_TXT"));
            Assert.False(FohlioStale.IsStale(ps, held, 1250, "usd", snap, 1250, "US$", "FOHLIO_UNIT_COST_NR", "FOHLIO_CURRENCY_TXT"));
        }

        [Fact]
        public void L2Foh7_AFileReSavedAsAnsiIsRefused()
        {
            string p = Path.Combine(Path.GetTempPath(), "foh_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                // "Café" in Windows-1252: 0xE9 alone is not valid UTF-8.
                File.WriteAllBytes(p, new byte[] { (byte)'K', (byte)'\n', (byte)'C', (byte)'a', (byte)'f', 0xE9, (byte)'\n' });
                Assert.Throws<InvalidDataException>(() => FohlioCsv.ReadUtf8Lines(p));

                File.WriteAllText(p, "Key,Finish\r\n101,600×600 – matt\r\n", new UTF8Encoding(true));
                var lines = FohlioCsv.ReadUtf8Lines(p);
                Assert.Equal("Key,Finish", lines[0]);
                Assert.Equal("101,600×600 – matt", lines[1]);
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void L2Foh1_3_6_TheCommandsUseTheRules()
        {
            string cmd = Src("StingTools", "ExLink", "FohlioCommands.cs");
            // currency excluded from the text write path
            Assert.Contains("!string.Equals(c.Param, ParamRegistry.FOHLIO_CURRENCY", cmd);
            // a price cell's currency must agree with the column
            Assert.Contains("but the currency column says", cmd);
            Assert.Contains("FohlioCsv.ReadUtf8Lines(path)", cmd);
            Assert.Contains("FohlioStale.IsStale(", cmd);
            Assert.DoesNotContain("File.ReadAllLines(path)", Src("StingTools", "ExLink", "FohlioFinishesCommands.cs"));

            string kpi = Src("StingTools", "Commands", "Kpi", "OwnerKpiDashboardCommand.cs");
            Assert.Contains("map.LoadedFromProject", kpi);
            Assert.Contains("FohlioStale.IsStale(", kpi);
        }
    }
}
