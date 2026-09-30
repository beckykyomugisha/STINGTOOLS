// StingTools/Core/Delivery/MidpCsv.cs
//
// The MIDP / TIDP CSV reader, Revit-free so it can be tested against the real KUT template
// (GUIDES/KUT_MIDP_TEMPLATE.csv). MidpDriftReportCommand and MidpImportCommand both parse
// through here, so the two cannot disagree about which column is which.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.Core.Delivery
{
    public static class MidpCsv
    {
        public static List<DeliverablePlanItem> Parse(IReadOnlyList<string> lines, out int skipped, out int relativeOnly, DateTime? mobilisation)
        {
            skipped = 0; relativeOnly = 0;
            var rows = new List<DeliverablePlanItem>();
            if (lines == null || lines.Count < 2) return rows;
            static string Norm(string h) => new string((h ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var header = SplitCsv(lines[0]).Select(Norm).ToList();
            int Ix(params string[] names)
            {
                foreach (var n in names) { int i = header.IndexOf(Norm(n)); if (i >= 0) return i; }
                return -1;
            }
            int iCode = Ix("ref", "code", "doc number", "document number", "container");
            int iTitle = Ix("title", "deliverable", "name", "description");
            if (iTitle == iCode) iTitle = -1;
            int iDisc = Ix("discipline", "disc");
            int iMile = Ix("milestone", "stage", "data drop");
            int iDate = Ix("planned date", "planned", "due date", "date");
            int iRel = Ix("planned rel month", "rel month", "relative month");
            int iActual = Ix("actual date", "actual", "issued date");
            int iSuit = Ix("required suitability", "suitability", "s code");
            int iTidp = Ix("tidp ref", "tidp");
            int iRag = Ix("rag");

            for (int r = 1; r < lines.Count; r++)
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                var c = SplitCsv(lines[r]);
                string code = Get(c, iCode);
                if (string.IsNullOrWhiteSpace(code)) { continue; }
                string rel = Get(c, iRel).Trim();
                if (!TryParseDate(Get(c, iDate), out var pd))
                {
                    int months;
                    bool isRel = rel.Length > 1 && (rel[0] == 'M' || rel[0] == 'm') && int.TryParse(rel.Substring(1), out months);
                    if (isRel && mobilisation.HasValue)
                        pd = mobilisation.Value.Date.AddMonths(int.Parse(rel.Substring(1)));
                    else
                    {
                        if (isRel) relativeOnly++; else skipped++;
                        continue;
                    }
                }
                rows.Add(new DeliverablePlanItem
                {
                    Code = code,
                    Title = Get(c, iTitle),
                    Discipline = Get(c, iDisc),
                    Milestone = Get(c, iMile),
                    PlannedDate = pd,
                    RequiredSuitability = Get(c, iSuit).Trim(),
                    PlanActualDate = TryParseDate(Get(c, iActual), out var ad) ? ad : (DateTime?)null,
                    PlannedRelMonth = rel,
                    TidpRef = Get(c, iTidp).Trim(),
                    Rag = Get(c, iRag).Trim(),
                });
            }
            return rows;
        }

        public static bool TryParseDate(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
                || DateTime.TryParseExact(s.Trim(), new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MMM-yy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }

        public static List<string> SplitCsv(string line)
        {
            var outp = new List<string>(); var sb = new StringBuilder(); bool q = false;
            foreach (char ch in line ?? "")
            {
                if (ch == '"') q = !q;
                else if (ch == ',' && !q) { outp.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            outp.Add(sb.ToString());
            return outp;
        }

        private static string Get(List<string> c, int i) => (i >= 0 && i < c.Count) ? c[i].Trim() : "";
    }
}
