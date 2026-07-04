// ══════════════════════════════════════════════════════════════════════════
//  ErpFormats.cs — pure QuickBooks (IIF) + Sage (CSV) cost-import builders. PM-3.
//
//  The audit (§5 capability 10) flagged an accounting export as the Uganda-
//  pragmatic bridge — QuickBooks / Sage / Excel are what SMEs here actually run.
//  BoqErpExporter already writes a generic flat CSV and a P6 XML; this adds the
//  two accounting-package-native formats:
//
//    • QuickBooks IIF — a tab-delimited General Journal transaction: each cost
//      line is a debit SPL to its cost account; one balancing credit TRNS to the
//      control account, so debits − credit = 0 (a valid importable journal).
//    • Sage CSV — a nominal-journal CSV (Type, Nominal, Date, Reference, Details,
//      Net, TaxCode, TaxAmount) with one JD (journal debit) row per cost line and
//      a JC (journal credit) balancing row, the shape Sage 50 import expects.
//
//  Both are pure string builders over a tiny ErpRow projection (no Revit, no I/O)
//  so they unit-test headlessly in StingTools.Boq.Tests with worked totals.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.BOQ
{
    /// <summary>One posting line for an accounting export.</summary>
    public class ErpRow
    {
        public string CostAccount { get; set; } = "Construction Costs";
        public string ClassName { get; set; } = "";      // QuickBooks class / Sage cost-centre
        public string Memo { get; set; } = "";
        public double Amount { get; set; }                // positive = a cost (debit)
        public string DocNum { get; set; } = "";
    }

    public static class ErpFormats
    {
        /// <summary>QuickBooks IIF general-journal text. <paramref name="date"/> is
        /// formatted MM/dd/yyyy (US, as IIF requires). The control account is
        /// credited the total; each row debits its cost account.</summary>
        public static string BuildIif(
            IEnumerable<ErpRow> rows, DateTime date,
            string controlAccount = "Accounts Payable", string docNum = "BOQ")
        {
            var list = (rows ?? Enumerable.Empty<ErpRow>()).Where(r => r != null).ToList();
            string d = date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            double total = MoneyRound(list.Sum(r => r.Amount));

            var sb = new StringBuilder();
            sb.Append("!TRNS\tTRNSTYPE\tDATE\tACCNT\tNAME\tCLASS\tAMOUNT\tDOCNUM\tMEMO\n");
            sb.Append("!SPL\tTRNSTYPE\tDATE\tACCNT\tNAME\tCLASS\tAMOUNT\tDOCNUM\tMEMO\n");
            sb.Append("!ENDTRNS\n");

            // TRNS = balancing credit to the control account (negative).
            sb.Append($"TRNS\tGENERAL JOURNAL\t{d}\t{Tab(controlAccount)}\t\t\t" +
                      $"{Money(-total)}\t{Tab(docNum)}\tBOQ cost import\n");
            foreach (var r in list)
                sb.Append($"SPL\tGENERAL JOURNAL\t{d}\t{Tab(r.CostAccount)}\t\t{Tab(r.ClassName)}\t" +
                          $"{Money(MoneyRound(r.Amount))}\t{Tab(string.IsNullOrEmpty(r.DocNum) ? docNum : r.DocNum)}\t{Tab(r.Memo)}\n");
            sb.Append("ENDTRNS\n");
            return sb.ToString();
        }

        /// <summary>Sage 50 nominal-journal CSV. One JD row per cost line plus a
        /// single JC balancing row to the control nominal.</summary>
        public static string BuildSageCsv(
            IEnumerable<ErpRow> rows, DateTime date,
            string controlNominal = "2100", string reference = "BOQ")
        {
            var list = (rows ?? Enumerable.Empty<ErpRow>()).Where(r => r != null).ToList();
            string d = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            double total = MoneyRound(list.Sum(r => r.Amount));

            var sb = new StringBuilder();
            sb.Append("Type,Nominal,Date,Reference,Details,Net,TaxCode,TaxAmount\n");
            foreach (var r in list)
                sb.Append($"JD,{Csv(NominalFor(r))},{d},{Csv(reference)},{Csv(r.Memo)}," +
                          $"{Money(MoneyRound(r.Amount))},T9,0.00\n");
            sb.Append($"JC,{Csv(controlNominal)},{d},{Csv(reference)},BOQ cost import," +
                      $"{Money(total)},T9,0.00\n");
            return sb.ToString();
        }

        // A Sage nominal code: prefer an all-digit ClassName (a real nominal), else
        // the default cost nominal 5000.
        private static string NominalFor(ErpRow r)
            => !string.IsNullOrWhiteSpace(r.ClassName) && r.ClassName.All(char.IsDigit) ? r.ClassName : "5000";

        private static double MoneyRound(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        private static string Money(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        // IIF is tab-delimited — strip embedded tabs/newlines from a field.
        private static string Tab(string s) => (s ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");

        private static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
