// ══════════════════════════════════════════════════════════════════════════
//  CsvTable.cs — read a single-table CSV by COLUMN NAME (DSCH-2).
//  Revit-free (compiled into the test projects that need it).
//
//  Around 140 call sites indexed CSV fields by position. An inserted, removed
//  or reordered column then reads every later field from its neighbour, with
//  no error: D6 inserting PROD into cost_rates_5d.csv silently broke the 5D
//  Cost Trace. Every single-table CSV header is pinned in tools/data_schemas.json
//  (CI fails on drift), and readers use this so a column move cannot misread.
//
//      var t = CsvTable.Parse(File.ReadAllLines(path), StingToolsApp.ParseCsvLine);
//      int nameCol = t.Col("MAT_NAME");            // -1 when absent
//      foreach (var r in t.Rows)
//          string name = r["MAT_NAME"];            // "" when absent or short
//
//  Leading '#' comment and blank lines are skipped; the first other line is
//  the header; later '#' lines are skipped too. Names match case-insensitively,
//  ignoring a BOM and surrounding whitespace/quotes. Multi-section files (the
//  TAG_CONFIG packs, BOQ_TEMPLATE) are NOT single tables - do not use this there.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> _index;
        public IReadOnlyList<string> Header { get; }
        public IReadOnlyList<CsvRow> Rows { get; }
        /// <summary>1-based line number of the header in the source, 0 when there was none.</summary>
        public int HeaderLine { get; }

        private CsvTable(List<string> header, Dictionary<string, int> index, List<CsvRow> rows, int headerLine)
        {
            Header = header; _index = index; HeaderLine = headerLine;
            Rows = rows;
            foreach (var r in rows) r.Table = this;
        }

        public static CsvTable Parse(IEnumerable<string> lines, Func<string, string[]> splitLine)
        {
            var header = new List<string>();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<CsvRow>();
            int ln = 0, headerLine = 0;
            if (lines != null && splitLine != null)
            {
                foreach (string raw in lines)
                {
                    ln++;
                    string line = raw?.TrimStart('﻿');
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    string[] f = splitLine(line) ?? Array.Empty<string>();
                    if (headerLine == 0)
                    {
                        headerLine = ln;
                        for (int i = 0; i < f.Length; i++)
                        {
                            string h = Clean(f[i]);
                            header.Add(h);
                            if (h.Length > 0 && !index.ContainsKey(h)) index[h] = i;
                        }
                        continue;
                    }
                    rows.Add(new CsvRow(f, ln));
                }
            }
            return new CsvTable(header, index, rows, headerLine);
        }

        /// <summary>Column index for <paramref name="name"/>, or -1 when the header lacks it.</summary>
        public int Col(string name) => name != null && _index.TryGetValue(Clean(name), out int i) ? i : -1;

        public bool Has(string name) => Col(name) >= 0;

        /// <summary>Names from <paramref name="required"/> the header does not carry.</summary>
        public List<string> Missing(params string[] required)
        {
            var missing = new List<string>();
            foreach (string r in required ?? Array.Empty<string>())
                if (!Has(r)) missing.Add(r);
            return missing;
        }

        private static string Clean(string h) => (h ?? "").Trim().TrimStart('﻿').Trim().Trim('"').Trim();
    }

    public sealed class CsvRow
    {
        internal CsvTable Table;
        public string[] Fields { get; }
        /// <summary>1-based line number in the source file.</summary>
        public int Line { get; }

        internal CsvRow(string[] fields, int line) { Fields = fields; Line = line; }

        /// <summary>The trimmed cell under <paramref name="column"/>; "" when the column
        /// is absent or the row is short.</summary>
        public string this[string column]
        {
            get
            {
                int i = Table?.Col(column) ?? -1;
                return i >= 0 && i < Fields.Length ? (Fields[i] ?? "").Trim() : "";
            }
        }

        /// <summary>Number of fields on this row (to detect a short or split row).</summary>
        public int Count => Fields.Length;
    }
}
