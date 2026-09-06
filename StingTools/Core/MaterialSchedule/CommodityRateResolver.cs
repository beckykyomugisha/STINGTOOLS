// ══════════════════════════════════════════════════════════════════════════
//  CommodityRateResolver.cs — MAT-SCHED commodity price list.
//
//  WHY THIS EXISTS: the BOQ's IRateProvider chain is element-scoped
//  (RateRequest.Element) and both shipped rate CSVs key on Revit CATEGORY, so
//  nothing in the codebase can price "one bag of cement". Constituent rows
//  currently resolve to (0, "None", 20) for every constituent.
//
//  An unpriced commodity stays visibly unpriced. Borrowing a neighbouring rate
//  would put a confident-looking number in a tender document.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class CommodityRate
    {
        public string CommodityKey = "";
        public string SupplierUnit = "";
        public double RateUGX;
        public string Source = "";      // "baseline" / "project" / "unpriced"

        /// <summary>Free text, round-tripped so a hand-edited file keeps its
        /// notes when the editor rewrites it.</summary>
        public string Description = "";
    }

    public sealed class CommodityRateResolver
    {
        private readonly Dictionary<string, CommodityRate> _baseline =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CommodityRate> _project =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unpriced =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CommodityRateResolver(IEnumerable<CommodityRate> baseline,
                                     IEnumerable<CommodityRate> projectOverrides)
        {
            foreach (var r in baseline ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _baseline[r.CommodityKey] = r;
            foreach (var r in projectOverrides ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _project[r.CommodityKey] = r;
        }

        /// <summary>Commodity keys asked for but not priced. Drives the export gate.</summary>
        public IReadOnlyCollection<string> UnpricedKeys => _unpriced;

        public CommodityRate Resolve(string commodityKey)
        {
            if (string.IsNullOrWhiteSpace(commodityKey))
                return new CommodityRate { CommodityKey = "", RateUGX = 0, Source = "unpriced" };

            if (_project.TryGetValue(commodityKey, out var p) && p.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = p.CommodityKey, SupplierUnit = p.SupplierUnit,
                    RateUGX = p.RateUGX, Source = "project"
                };

            if (_baseline.TryGetValue(commodityKey, out var b) && b.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = b.CommodityKey, SupplierUnit = b.SupplierUnit,
                    RateUGX = b.RateUGX, Source = "baseline"
                };

            _unpriced.Add(commodityKey);
            return new CommodityRate { CommodityKey = commodityKey, RateUGX = 0, Source = "unpriced" };
        }

        /// <summary>
        /// Parse the shipped CSV: CommodityKey,SupplierUnit,RateUGX,Description.
        /// '#' comment lines and blank lines are skipped; unparseable rows are
        /// skipped and reported through <paramref name="skipped"/> rather than
        /// silently dropped.
        /// </summary>
        public static List<CommodityRate> ParseCsv(IEnumerable<string> lines, out List<string> skipped)
        {
            var outList = new List<CommodityRate>();
            skipped = new List<string>();
            bool headerSeen = false;

            foreach (string raw in lines ?? Enumerable.Empty<string>())
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var parts = SplitCsvLine(line);
                if (!headerSeen && parts[0].Trim().Equals("CommodityKey", StringComparison.OrdinalIgnoreCase))
                { headerSeen = true; continue; }

                if (parts.Length < 3) { skipped.Add(line); continue; }
                if (!double.TryParse(parts[2].Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double rate))
                { skipped.Add(line); continue; }

                outList.Add(new CommodityRate
                {
                    CommodityKey = parts[0].Trim(),
                    SupplierUnit = parts[1].Trim(),
                    RateUGX = rate,
                    Source = "baseline",
                    Description = parts.Length > 3 ? parts[3].Trim() : ""
                });
            }
            return outList;
        }

        /// <summary>
        /// Split one CSV line, honouring double-quoted fields and "" escapes.
        ///
        /// The naive Split(',') this replaces corrupted any row whose
        /// description carried a comma — and a description is free text a
        /// quantity surveyor writes, so commas are normal. It failed by
        /// SHIFTING the columns: the rate came from the wrong field and either
        /// refused to parse (the row vanished into `skipped`) or parsed as a
        /// different number entirely.
        /// </summary>
        internal static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c != '"') { sb.Append(c); continue; }
                    // "" inside a quoted field is one literal quote.
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        /// <summary>Quote a field only when it needs it, and escape quotes by doubling.</summary>
        internal static string CsvField(string value)
        {
            string v = value ?? "";
            bool needs = v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0
                      || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0
                      || v != v.Trim();
            return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        /// <summary>
        /// Render project rate rows as the same CSV shape the parser reads.
        ///
        /// The counterpart to ParseCsv, and the reason the rate editor can exist
        /// at all: until this, NOTHING in the codebase wrote a rate file, so the
        /// only way to price a commodity was to hand-type a key — and 21 of the
        /// 25 keys the first real export asked for carry a non-ASCII em dash,
        /// which an exact OrdinalIgnoreCase lookup will miss in silence.
        /// </summary>
        public static List<string> WriteCsv(IEnumerable<CommodityRate> rows, string headerNote)
        {
            var lines = new List<string>
            {
                "# Project commodity rates — these WIN over the corporate baseline, by CommodityKey.",
                "# Written by the STING rate editor. Safe to edit by hand, to copy to another",
                "# project, and to keep under version control."
            };
            if (!string.IsNullOrWhiteSpace(headerNote))
                foreach (string l in headerNote.Split('\n'))
                    lines.Add("# " + l.TrimEnd());
            lines.Add("CommodityKey,SupplierUnit,RateUGX,Description");

            foreach (var r in (rows ?? Enumerable.Empty<CommodityRate>())
                        .Where(r => r != null && !string.IsNullOrWhiteSpace(r.CommodityKey))
                        .OrderBy(r => r.CommodityKey, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Join(",",
                    CsvField(r.CommodityKey),
                    CsvField(r.SupplierUnit),
                    r.RateUGX.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    CsvField(r.Description)));
            }
            return lines;
        }
    }
}
