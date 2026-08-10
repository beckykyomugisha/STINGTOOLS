using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// D6 / gate 3.2 — the product tier, keyed on (DISC, PROD).
    ///
    /// Every door priced at UGX 1,665,000 — fire door and cupboard door alike —
    /// because the rate table had no product tier and category was consulted first.
    /// Four diagnoses of that defect were wrong before this one, so the gate is
    /// pinned here rather than left to a manual check.
    ///
    /// Why (DISC, PROD) and not PROD alone: Air Terminals carries TWO products —
    /// a mechanical air terminal (ATU, UGX 555,000) and a lightning air terminal
    /// (LAT, UGX 666,000). Both are GRL in ProdMap. Keying on PROD alone collapses
    /// them into one rate, destroying the only real product differentiation the
    /// table had. DISC (M vs E) separates them without inventing a PROD code and
    /// without migrating ProdMap, which would touch every tag in every model.
    ///
    /// These assert the DATA and the key construction. The provider's pass ORDER
    /// lives in RateProviders.cs, which imports Autodesk.Revit.DB and cannot run
    /// headlessly — that half is verified by construction, not here.
    /// </summary>
    public class RateProductTierD6Tests
    {
        private static string CsvPath =>
            Path.Combine(AppContext.BaseDirectory, "Data", "cost_rates_5d.csv");

        /// <summary>Mirrors BOQCostManager.LoadCsvRatesUncached's 8-column branch.</summary>
        private static Dictionary<string, (double rate, string unit)> Load()
        {
            var rates = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
            void Put(string k, double r, string u)
            {
                if (!string.IsNullOrWhiteSpace(k) && !rates.ContainsKey(k)) rates[k] = (r, u);
            }

            var lines = File.ReadAllLines(CsvPath);
            foreach (var line in lines.Skip(1))
            {
                var c = line.Split(',');
                if (c.Length < 8) continue;
                if (!double.TryParse(c[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double ugx))
                    continue;
                string cat = c[0].Trim(), prod = c[1].Trim(), mat = c[2].Trim(),
                       disc = c[3].Trim(), unit = c[6].Trim();
                if (prod.Length > 0 && disc.Length > 0) Put($"{disc}|{prod}", ugx, unit);
                Put(cat, ugx, unit);
                Put(mat, ugx, unit);
            }
            return rates;
        }

        [Fact]
        public void The_Csv_Carries_A_Prod_Column()
        {
            // D7: the product tier is a real column now, not a code smuggled into the
            // category name.
            string header = File.ReadLines(CsvPath).First();
            Assert.StartsWith("Category,PROD,", header);
        }

        [Fact]
        public void Gate_3_2_Two_Products_In_One_Category_Price_Differently()
        {
            var rates = Load();

            Assert.True(rates.ContainsKey("M|GRL"), "mechanical air terminal key missing");
            Assert.True(rates.ContainsKey("E|GRL"), "lightning air terminal key missing");

            double mech = rates["M|GRL"].rate;
            double lps = rates["E|GRL"].rate;

            Assert.Equal(555000, mech);
            Assert.Equal(666000, lps);

            // THE GATE. If these are ever equal again, the product tier has collapsed
            // and every air terminal is priced as whichever row loaded first.
            Assert.NotEqual(mech, lps);
        }

        [Fact]
        public void A_Door_Resolves_At_Product_Level_Not_Only_By_Category()
        {
            var rates = Load();
            // ProdMap gives Doors -> DR; the CSV previously keyed DOR, so the product
            // pass could never match. Both keys now resolve.
            Assert.True(rates.ContainsKey("A|DR"), "door product key missing — ProdMap says DR");
            Assert.Equal(rates["Doors"].rate, rates["A|DR"].rate);
        }

        [Fact]
        public void Promoted_Rows_Now_Carry_A_Real_Revit_Category()
        {
            // D7 — these were products written into the category column, so nothing
            // could ever match them: no element has a category called "Pipe Systems".
            var cats = File.ReadLines(CsvPath).Skip(1)
                .Select(l => l.Split(',')[0].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.DoesNotContain("Pipe Systems", cats);
            Assert.DoesNotContain("Duct Systems", cats);
            Assert.DoesNotContain("Structural Foundation", cats);   // singular typo-twin
            Assert.Contains("Pipes", cats);
            Assert.Contains("Ducts", cats);
        }

        [Fact]
        public void Every_Rate_Row_Declares_A_Unit()
        {
            // 3.5 groundwork: two block families sat at UGX 2,220 and UGX 96,200 with
            // nothing to say which was per block and which per m². A rate without a
            // unit is not a rate.
            var missing = File.ReadLines(CsvPath).Skip(1)
                .Select(l => l.Split(','))
                .Where(c => c.Length >= 8 && string.IsNullOrWhiteSpace(c[6]))
                .ToList();
            Assert.Empty(missing);
        }
    }
}
