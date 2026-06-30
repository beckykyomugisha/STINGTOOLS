using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    // PM-3 — QuickBooks IIF + Sage CSV cost-import builders.
    public class ErpFormatsTests
    {
        private static List<ErpRow> Rows() => new List<ErpRow>
        {
            new ErpRow { CostAccount = "Construction:14", ClassName = "5000", Memo = "Brickwork", Amount = 1_000_000, DocNum = "14.1" },
            new ErpRow { CostAccount = "Construction:33", ClassName = "5100", Memo = "Pipework", Amount = 500_000.50, DocNum = "33.2" },
        };
        private static readonly DateTime D = new DateTime(2026, 6, 30);

        [Fact]
        public void Iif_has_journal_headers_and_balances_to_zero()
        {
            string iif = ErpFormats.BuildIif(Rows(), D, controlAccount: "Accounts Payable");
            Assert.Contains("!TRNS\tTRNSTYPE\tDATE\tACCNT", iif);
            Assert.Contains("ENDTRNS", iif);
            Assert.Contains("06/30/2026", iif);       // US date

            // Sum of every AMOUNT column (TRNS credit + SPL debits) must be 0.
            double sum = 0;
            foreach (var line in iif.Split('\n'))
            {
                if (!(line.StartsWith("TRNS\t") || line.StartsWith("SPL\t"))) continue;
                var cols = line.Split('\t');
                sum += double.Parse(cols[6], System.Globalization.CultureInfo.InvariantCulture);
            }
            Assert.Equal(0.0, Math.Round(sum, 2));
        }

        [Fact]
        public void Iif_credit_equals_minus_total()
        {
            string iif = ErpFormats.BuildIif(Rows(), D);
            var trns = iif.Split('\n').First(l => l.StartsWith("TRNS\t")).Split('\t');
            Assert.Equal("-1500000.50", trns[6]);     // −(1,000,000 + 500,000.50)
        }

        [Fact]
        public void Sage_csv_has_header_jd_rows_and_balancing_jc()
        {
            string csv = ErpFormats.BuildSageCsv(Rows(), D, controlNominal: "2100");
            var lines = csv.TrimEnd('\n').Split('\n');
            Assert.StartsWith("Type,Nominal,Date,Reference,Details,Net,TaxCode,TaxAmount", lines[0]);
            Assert.Equal(2, lines.Count(l => l.StartsWith("JD,")));
            var jc = lines.Single(l => l.StartsWith("JC,"));
            Assert.Contains("2100", jc);
            Assert.Contains("1500000.50", jc);        // JC = total of the JD rows
            Assert.Contains("30/06/2026", csv);       // UK date
        }

        [Fact]
        public void Empty_rows_produce_zero_balanced_documents()
        {
            string iif = ErpFormats.BuildIif(new List<ErpRow>(), D);
            Assert.Contains("0.00", iif);             // TRNS credit of -0.00/0.00
            string sage = ErpFormats.BuildSageCsv(new List<ErpRow>(), D);
            Assert.Single(sage.TrimEnd('\n').Split('\n').Where(l => l.StartsWith("JC,")));
        }
    }
}
