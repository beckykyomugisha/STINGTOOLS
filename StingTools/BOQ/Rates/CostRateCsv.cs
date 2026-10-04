// ══════════════════════════════════════════════════════════════════════════
//  CostRateCsv.cs — the ONE reader of the cost-rate CSV layout
//  (cost_rates_5d.csv and any file named by TagConfig.CostRatesFileName).
//
//  WHY. Three readers each parsed this file by column POSITION:
//  BOQCostManager.LoadCsvRates, Scheduling4DEngine's 5D Cost Trace and the Cost
//  File Browser's header check. D6 inserted a PROD column at index 1, and every
//  index after it moved. LoadCsvRates was taught the new layout by a second
//  positional branch; the 5D Cost Trace was not, so it read MAT_DISCIPLINE
//  ("A", "M") as the USD rate, every TryParse failed, and the command reported
//  "No cost rates found" against a 43-row file. Nothing logged it.
//
//  So columns are found by HEADER NAME here, once, and every reader asks this
//  type. Inserting, removing or reordering a column moves nothing. The column
//  vocabulary below must match the schema in tools/data_schemas.json — a test
//  (CostRateCsvTests) holds the two together.
//
//  Revit-free: compiled into StingTools.Boq.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StingTools.BOQ.Rates
{
    /// <summary>One data row of a cost-rate CSV, read by column name.</summary>
    public sealed class CostRateRow
    {
        public int LineNumber;
        public string Category = "";
        public string Prod = "";
        public string MatCode = "";
        public string Discipline = "";
        /// <summary>null when the file has no USD column or the cell is not a number.</summary>
        public double? RateUsd;
        /// <summary>null when the file has no UGX column or the cell is not a number.</summary>
        public double? RateUgx;
        public string Unit = "";
        public string Description = "";

        /// <summary>DSCH-26 — a rate cell holding NIL / INCL / INCL:&lt;ref&gt; instead of a
        /// number. Priced (the default) for every numeric row.</summary>
        public RateOutcome Outcome = RateOutcome.Priced;
        /// <summary>The reference after INCL: — where the cost is carried. Empty otherwise.</summary>
        public string IncludedIn = "";

        /// <summary>DSCH-26 — the row's rate is a bare 0 with no NIL / INCL declaration.
        /// It stays in <see cref="CostRateCsv.Result.Rows"/> (it is what the file says)
        /// but is NOT a rate: the table builder leaves it out, so the item is priced by
        /// the next match or reported as not priced.</summary>
        public bool IsUndeclaredZero =>
            Outcome == RateOutcome.Priced
            && ((RateUgx.HasValue && RateUgx.Value == 0 && !(RateUsd > 0))
                || (!RateUgx.HasValue && RateUsd.HasValue && RateUsd.Value == 0));
    }

    /// <summary>DSCH-26 — a key the rate file declares Nil or Included.</summary>
    public sealed class DeclaredRate
    {
        public RateOutcome Outcome;
        public string IncludedIn = "";
        public string Unit = "each";
    }

    /// <summary>
    /// Column positions resolved from a header row. Every lookup is by name,
    /// case-insensitive, whitespace- and BOM-tolerant.
    /// </summary>
    public sealed class CostRateCsvLayout
    {
        // Canonical column names — the schema in tools/data_schemas.json.
        public const string ColCategory = "Category";
        public const string ColProd = "PROD";
        public const string ColMatCode = "MAT_CODE";
        public const string ColDiscipline = "MAT_DISCIPLINE";
        public const string ColRateUsd = "Unit_Rate_USD";
        public const string ColRateUgx = "Unit_Rate_UGX";
        public const string ColUnit = "Unit";
        public const string ColDescription = "Description";

        /// <summary>Every column the readers understand, in the shipped order.</summary>
        public static readonly string[] KnownColumns =
        {
            ColCategory, ColProd, ColMatCode, ColDiscipline,
            ColRateUsd, ColRateUgx, ColUnit, ColDescription,
        };

        // Legacy 3-column rate card ("Category,Rate,Unit"): accepted, never written.
        private const string LegacyRate = "Rate";

        public int Category = -1, Prod = -1, MatCode = -1, Discipline = -1,
                   RateUsd = -1, RateUgx = -1, Unit = -1, Description = -1;

        /// <summary>True for the legacy Category,Rate,Unit card; its one rate column
        /// is treated as UGX, as LoadCsvRates always has.</summary>
        public bool IsLegacyRateCard;

        /// <summary>Header columns no reader understands. Reported, never silently used.</summary>
        public readonly List<string> UnknownColumns = new List<string>();

        public bool HasAnyRate => RateUsd >= 0 || RateUgx >= 0;

        public static CostRateCsvLayout FromHeader(IReadOnlyList<string> header)
        {
            var l = new CostRateCsvLayout();
            if (header == null) return l;
            for (int i = 0; i < header.Count; i++)
            {
                string h = Normalise(header[i]);
                if (Eq(h, ColCategory)) l.Category = i;
                else if (Eq(h, ColProd)) l.Prod = i;
                else if (Eq(h, ColMatCode)) l.MatCode = i;
                else if (Eq(h, ColDiscipline)) l.Discipline = i;
                else if (Eq(h, ColRateUsd)) l.RateUsd = i;
                else if (Eq(h, ColRateUgx)) l.RateUgx = i;
                else if (Eq(h, ColUnit)) l.Unit = i;
                else if (Eq(h, ColDescription)) l.Description = i;
                else if (Eq(h, LegacyRate)) { l.RateUgx = i; l.IsLegacyRateCard = true; }
                else if (h.Length > 0) l.UnknownColumns.Add(h);
            }
            // A legacy card with a differently-spelt rate column ("RatePerUnit",
            // "Rate_UGX"): the positional reader took column 1 whatever it was
            // called. Keep accepting it, but only when it is visibly a rate column
            // and nothing better exists, and say so through IsLegacyRateCard.
            if (!l.HasAnyRate)
            {
                for (int i = 0; i < header.Count; i++)
                {
                    string h = Normalise(header[i]);
                    if (h.IndexOf("rate", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (h.IndexOf("usd", StringComparison.OrdinalIgnoreCase) >= 0) l.RateUsd = i;
                    else l.RateUgx = i;
                    l.IsLegacyRateCard = true;
                    l.UnknownColumns.Remove(h);
                    break;
                }
            }
            return l;
        }

        /// <summary>
        /// Columns a cost-rate file must carry to price anything. Empty = usable.
        /// The Cost File Browser shows this list instead of its own hard-coded one.
        /// </summary>
        public List<string> MissingRequired()
        {
            var missing = new List<string>();
            if (Category < 0) missing.Add(ColCategory);
            if (!HasAnyRate) missing.Add(ColRateUgx + " or " + ColRateUsd);
            if (Unit < 0) missing.Add(ColUnit);
            return missing;
        }

        public CostRateRow Read(IReadOnlyList<string> cols, int lineNumber)
        {
            var r = new CostRateRow { LineNumber = lineNumber };
            r.Category = Cell(cols, Category);
            r.Prod = Cell(cols, Prod);
            r.MatCode = Cell(cols, MatCode);
            r.Discipline = Cell(cols, Discipline);
            string usdCell = Cell(cols, RateUsd), ugxCell = Cell(cols, RateUgx);
            r.RateUsd = Num(usdCell);
            r.RateUgx = Num(ugxCell);
            // DSCH-26 — a declaration in either rate cell. The other cell may be
            // blank or repeat the same declaration; a number there contradicts it,
            // and Parse reports the row.
            if (RateOutcomeToken.TryParse(ugxCell, out var oUgx, out string refUgx))
            { r.Outcome = oUgx; r.IncludedIn = refUgx; }
            else if (RateOutcomeToken.TryParse(usdCell, out var oUsd, out string refUsd))
            { r.Outcome = oUsd; r.IncludedIn = refUsd; }
            r.Unit = Cell(cols, Unit);
            r.Description = Cell(cols, Description);
            return r;
        }

        private static string Cell(IReadOnlyList<string> cols, int i)
            => (i >= 0 && cols != null && i < cols.Count) ? (cols[i] ?? "").Trim() : "";

        // Invariant culture, always: the file is authored with '.' decimals and a
        // UK/Uganda Windows locale would otherwise read 85.00 as 8500.
        private static double? Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                       CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        private static string Normalise(string h)
            => (h ?? "").Trim().TrimStart('﻿').Trim().Trim('"').Trim();

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses a whole cost-rate file. Comment (#) and blank lines are skipped.</summary>
    public static class CostRateCsv
    {
        public sealed class Result
        {
            public CostRateCsvLayout Layout = new CostRateCsvLayout();
            public readonly List<CostRateRow> Rows = new List<CostRateRow>();
            /// <summary>Rows dropped, each with the reason — never dropped silently.</summary>
            public readonly List<string> Problems = new List<string>();
        }

        public static Result Parse(IEnumerable<string> lines, Func<string, string[]> parseLine)
        {
            var res = new Result();
            if (lines == null || parseLine == null) { res.Problems.Add("no input"); return res; }
            bool headerSeen = false;
            int ln = 0;
            foreach (string raw in lines)
            {
                ln++;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.TrimStart('﻿').TrimStart().StartsWith("#")) continue;
                string[] cols = parseLine(raw);
                if (!headerSeen)
                {
                    headerSeen = true;
                    res.Layout = CostRateCsvLayout.FromHeader(cols);
                    foreach (string m in res.Layout.MissingRequired())
                        res.Problems.Add($"line {ln}: header has no '{m}' column");
                    continue;
                }
                var row = res.Layout.Read(cols, ln);
                if (row.Outcome != RateOutcome.Priced)
                {
                    if (row.RateUsd > 0 || row.RateUgx > 0)
                    {
                        res.Problems.Add($"line {ln}: rate is declared {RateOutcomeToken.ToToken(row.Outcome, row.IncludedIn)} " +
                                         "and also carries a number — contradictory, row skipped");
                        continue;
                    }
                    res.Rows.Add(row);
                    continue;
                }
                if (row.RateUsd == null && row.RateUgx == null)
                {
                    res.Problems.Add($"line {ln}: no numeric rate — row skipped");
                    continue;
                }
                if (row.IsUndeclaredZero)
                    res.Problems.Add($"line {ln}: zero rate without NIL / INCL — treated as not priced " +
                                     "(write NIL for a deliberate nil rate, INCL or INCL:<item> when it is included elsewhere)");
                res.Rows.Add(row);
            }
            if (!headerSeen) res.Problems.Add("file has no header row");
            return res;
        }

        /// <summary>
        /// The UGX rate table BOQCostManager.LoadCsvRates serves to CsvRateLookup.
        /// Each row registers, most specific first: DISC|PROD (D6 — PROD alone is not
        /// unique: ATU and LAT are both GRL), then Category, then MAT_CODE.
        ///
        /// CA-1 one-wins de-duplication: the first row for a key wins (top of file is
        /// authoritative); each later collision is appended to
        /// <paramref name="duplicateKeys"/> so the caller can log it instead of a
        /// silent last-row-wins overwrite.
        /// </summary>
        public static Dictionary<string, (double rate, string unit)> ToUgxRateTable(
            Result parsed, List<string> duplicateKeys = null)
            => ToTables(parsed, out _, duplicateKeys);

        /// <summary>
        /// DSCH-26 — both tables a rate file yields: the UGX rates, and the keys the
        /// file declares Nil or Included (<paramref name="declared"/>). One key belongs
        /// to one row — the first, in either table — so a NIL row cannot be shadowed by a
        /// later priced row for the same key, or the reverse. An undeclared 0 claims no
        /// key: it is not a rate, so a later row (or the next, less specific pass) may
        /// price the item.
        /// </summary>
        public static Dictionary<string, (double rate, string unit)> ToTables(
            Result parsed, out Dictionary<string, DeclaredRate> declared, List<string> duplicateKeys = null)
        {
            var rates = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase);
            var decl = new Dictionary<string, DeclaredRate>(StringComparer.OrdinalIgnoreCase);
            declared = decl;
            if (parsed == null) return rates;
            foreach (var row in parsed.Rows)
            {
                string unit = string.IsNullOrEmpty(row.Unit) ? "each" : row.Unit;
                bool isDeclared = row.Outcome != RateOutcome.Priced;
                double ugx = 0;
                if (!isDeclared)
                {
                    if (row.RateUgx is not double v) continue;
                    if (v <= 0) continue;   // undeclared zero — reported by Parse, not a rate
                    ugx = v;
                }
                if (row.Prod.Length > 0 && row.Discipline.Length > 0)
                    Put(row.Discipline + "|" + row.Prod);
                Put(row.Category);
                Put(row.MatCode);

                void Put(string key)
                {
                    if (string.IsNullOrEmpty(key)) return;
                    if (rates.ContainsKey(key) || decl.ContainsKey(key)) { duplicateKeys?.Add(key); return; }
                    if (isDeclared)
                        decl[key] = new DeclaredRate { Outcome = row.Outcome, IncludedIn = row.IncludedIn ?? "", Unit = unit };
                    else
                        rates[key] = (ugx, unit);
                }
            }
            return rates;
        }
    }
}
