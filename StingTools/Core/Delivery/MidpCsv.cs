// StingTools/Core/Delivery/MidpCsv.cs
//
// The MIDP / TIDP CSV reader, Revit-free so it can be tested against the real KUT template
// (GUIDES/KUT_MIDP_TEMPLATE.csv). MidpDriftReportCommand and MidpImportCommand both parse
// through here, so the two cannot disagree about which column is which.
//
// DATES (E5). DateTime.TryParse(InvariantCulture) used to run first, and the invariant
// culture is MONTH-first: "05/03/2027" (5 March, as a UK / Uganda MIDP writes it) was read as
// 3 May. Now: ISO yyyy-MM-dd first, then a numeric a/b/yyyy in ONE configured order -
// day-first by default (the UK / Uganda convention). A numeric date the chosen order cannot
// read (month 13+, day 32, …) is REFUSED and counted with its reason, never re-read the other
// way round. Month-name forms ("05-Mar-27", "5 March 2027") are unambiguous and accepted.
//
// COLUMNS (E7). With no code column every row used to be dropped without a word. A missing
// required column now refuses the whole file and names it; a row with an empty code is
// counted as skipped.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StingTools.Core.Delivery
{
    /// <summary>How a numeric a/b/yyyy date is read. ISO yyyy-MM-dd is accepted either way.</summary>
    public enum MidpDateOrder
    {
        /// <summary>dd/MM/yyyy - the UK / Uganda convention, the default.</summary>
        DayFirst,
        /// <summary>MM/dd/yyyy - only when the project says so.</summary>
        MonthFirst,
    }

    /// <summary>Everything a MIDP parse found, including what it refused and why.</summary>
    public sealed class MidpParseResult
    {
        public List<DeliverablePlanItem> Rows { get; } = new List<DeliverablePlanItem>();
        /// <summary>Required columns the header lacks. Non-empty means the file was refused.</summary>
        public List<string> MissingColumns { get; } = new List<string>();
        /// <summary>Rows with no code (counted, never silently dropped).</summary>
        public int EmptyCode { get; set; }
        /// <summary>Rows whose planned date could not be read - see <see cref="DateProblems"/>.</summary>
        public int BadDate { get; set; }
        /// <summary>Rows with neither a planned date nor a relative month.</summary>
        public int NoDate { get; set; }
        /// <summary>Rows with only a relative month and no mobilisation date to anchor it.</summary>
        public int RelativeOnly { get; set; }
        /// <summary>F7: rows kept, but whose actual date could not be read, so they are NOT
        /// shown as delivered from the plan. Listed in <see cref="DateProblems"/>.</summary>
        public int BadActualDate { get; set; }
        /// <summary>The first few refused dates, each with the row and the reason.</summary>
        public List<string> DateProblems { get; } = new List<string>();

        public bool Refused => MissingColumns.Count > 0;
        /// <summary>Every row not imported for a reason other than a relative month.</summary>
        public int Skipped => EmptyCode + BadDate + NoDate;

        public string Describe()
        {
            if (Refused)
                return "The file was refused: the header has no " + string.Join(" / ", MissingColumns) + " column. " +
                       "Recognised headers include Ref / Code / Doc Number / Information Container ID, and " +
                       "Planned Date / Due Date or Planned Rel Month.";
            var parts = new List<string> { $"{Rows.Count} row(s) read" };
            if (EmptyCode > 0) parts.Add($"{EmptyCode} skipped - no code");
            if (NoDate > 0) parts.Add($"{NoDate} skipped - no planned date or relative month");
            if (BadDate > 0) parts.Add($"{BadDate} skipped - date not readable: " + string.Join("; ", DateProblems));
            if (RelativeOnly > 0) parts.Add($"{RelativeOnly} left out - relative month only, no mobilisation date");
            if (BadActualDate > 0) parts.Add($"{BadActualDate} read without their actual date - not readable (see above), so not counted as delivered");
            return string.Join("; ", parts) + ".";
        }
    }

    public static class MidpCsv
    {
        private const int MaxDateProblemsListed = 5;

        /// <summary>Backward-compatible form. <paramref name="skipped"/> now includes rows with an
        /// empty code (E7); a refused file returns no rows - use <see cref="ParseDetailed"/> to
        /// see why.</summary>
        public static List<DeliverablePlanItem> Parse(IReadOnlyList<string> lines, out int skipped, out int relativeOnly, DateTime? mobilisation)
        {
            var r = ParseDetailed(lines, mobilisation, MidpDateOrder.DayFirst);
            skipped = r.Skipped;
            relativeOnly = r.RelativeOnly;
            return r.Rows;
        }

        public static MidpParseResult ParseDetailed(IReadOnlyList<string> lines, DateTime? mobilisation,
            MidpDateOrder order = MidpDateOrder.DayFirst)
        {
            var result = new MidpParseResult();
            if (lines == null || lines.Count < 1) { result.MissingColumns.Add("header row"); return result; }
            static string Norm(string h) => new string((h ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var header = SplitCsv(lines[0]).Select(Norm).ToList();
            int Ix(params string[] names)
            {
                foreach (var n in names) { int i = header.IndexOf(Norm(n)); if (i >= 0) return i; }
                return -1;
            }
            int iCode = Ix("ref", "code", "doc number", "document number", "container",
                           "information container id", "information container", "container id", "container ref",
                           "document id", "doc id", "doc ref", "reference", "deliverable id", "deliverable ref");
            int iTitle = Ix("title", "deliverable", "name", "description", "information container name", "container name");
            if (iTitle == iCode) iTitle = -1;
            int iDisc = Ix("discipline", "disc");
            int iMile = Ix("milestone", "stage", "data drop");
            int iDate = Ix("planned date", "planned", "due date", "date", "planned delivery date", "delivery date");
            int iRel = Ix("planned rel month", "rel month", "relative month");
            int iActual = Ix("actual date", "actual", "issued date");
            int iSuit = Ix("required suitability", "suitability", "s code");
            int iTidp = Ix("tidp ref", "tidp");
            int iRag = Ix("rag");

            if (iCode < 0) result.MissingColumns.Add("code (Ref / Code / Information Container ID)");
            if (iDate < 0 && iRel < 0) result.MissingColumns.Add("planned date (Planned Date / Due Date / Planned Rel Month)");
            if (result.Refused) return result;

            for (int r = 1; r < lines.Count; r++)
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                var c = SplitCsv(lines[r]);
                if (c.All(string.IsNullOrWhiteSpace)) continue;   // ",,,," is a blank line, not a row
                string code = Get(c, iCode);
                if (string.IsNullOrWhiteSpace(code)) { result.EmptyCode++; continue; }
                string rel = Get(c, iRel).Trim();
                string rawDate = Get(c, iDate);
                DateTime pd;
                if (rawDate.Length > 0)
                {
                    if (!TryParseDate(rawDate, order, out pd, out string why))
                    {
                        result.BadDate++;
                        if (result.DateProblems.Count < MaxDateProblemsListed)
                            result.DateProblems.Add($"{code} '{rawDate}': {why}");
                        continue;
                    }
                }
                else
                {
                    bool isRel = rel.Length > 1 && (rel[0] == 'M' || rel[0] == 'm') && int.TryParse(rel.Substring(1), out _);
                    if (isRel && mobilisation.HasValue)
                        pd = mobilisation.Value.Date.AddMonths(int.Parse(rel.Substring(1)));
                    else
                    {
                        if (isRel) result.RelativeOnly++; else result.NoDate++;
                        continue;
                    }
                }
                result.Rows.Add(new DeliverablePlanItem
                {
                    Code = code,
                    Title = Get(c, iTitle),
                    Discipline = Get(c, iDisc),
                    Milestone = Get(c, iMile),
                    PlannedDate = pd,
                    RequiredSuitability = Get(c, iSuit).Trim(),
                    PlanActualDate = ReadActual(result, code, Get(c, iActual), order),
                    PlannedRelMonth = rel,
                    TidpRef = Get(c, iTidp).Trim(),
                    Rag = Get(c, iRag).Trim(),
                });
            }
            return result;
        }

        /// <summary>F7: an actual date that is present but unreadable is counted and listed,
        /// never silently read as "not delivered".</summary>
        private static DateTime? ReadActual(MidpParseResult result, string code, string raw, MidpDateOrder order)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (TryParseDate(raw, order, out var ad, out string why)) return ad;
            result.BadActualDate++;
            if (result.DateProblems.Count < MaxDateProblemsListed)
                result.DateProblems.Add($"{code} actual '{raw}': {why}");
            return null;
        }

        /// <summary>F6: a date already held as a value (a JSON date token's DateTime) is used as
        /// it is; only text is parsed. Formatting a DateTime back to text gave an invariant
        /// month-first string the day-first parser refused.</summary>
        public static bool TryDateValue(object value, out DateTime dt)
        {
            switch (value)
            {
                case DateTime d: dt = d; return true;
                case DateTimeOffset o: dt = o.UtcDateTime; return true;
                case string s: return TryParseDate(s, out dt);
                default: dt = default; return false;
            }
        }

        /// <summary>Day-first, as before E5 for callers that only need yes/no.</summary>
        public static bool TryParseDate(string s, out DateTime dt) => TryParseDate(s, MidpDateOrder.DayFirst, out dt, out _);

        private static readonly Regex IsoPrefix = new Regex(@"^\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant);
        private static readonly Regex Numeric = new Regex(@"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{4})$", RegexOptions.CultureInvariant);

        /// <summary>Read a MIDP date. ISO yyyy-MM-dd (with or without a time) first; then a
        /// numeric a/b/yyyy in <paramref name="order"/> only; then month-name forms. False, with
        /// <paramref name="reason"/>, for anything else - a numeric date is never re-read in the
        /// other order to make it fit.</summary>
        public static bool TryParseDate(string s, MidpDateOrder order, out DateTime dt, out string reason)
        {
            dt = DateTime.MinValue;
            reason = null;
            string t = (s ?? "").Trim();
            if (t.Length == 0) { reason = "empty"; return false; }

            if (IsoPrefix.IsMatch(t))
            {
                if (DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return true;
                // ISO with a time (deliverables.json timestamps): the date part is unambiguous.
                if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return true;
                reason = "not a valid yyyy-MM-dd date";
                return false;
            }

            var m = Numeric.Match(t);
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int b = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int y = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int day = order == MidpDateOrder.DayFirst ? a : b;
                int month = order == MidpDateOrder.DayFirst ? b : a;
                string fmt = order == MidpDateOrder.DayFirst ? "dd/MM/yyyy" : "MM/dd/yyyy";
                if (y < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(y, month))
                {
                    reason = $"not a valid {fmt} date (the MIDP date order is {fmt}; write it as yyyy-MM-dd to be unambiguous)";
                    return false;
                }
                dt = new DateTime(y, month, day);
                return true;
            }

            if (DateTime.TryParseExact(t, new[] { "dd-MMM-yy", "d-MMM-yy", "dd-MMM-yyyy", "d-MMM-yyyy", "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return true;

            reason = "not a recognised date (use yyyy-MM-dd)";
            return false;
        }

        /// <summary>"dmy" / "dd/mm/yyyy" / "day" -> DayFirst; "mdy" / "mm/dd/yyyy" / "month" ->
        /// MonthFirst; empty -> DayFirst. Anything else is null - not a guess.</summary>
        public static MidpDateOrder? ParseOrder(string s)
        {
            string t = new string((s ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            if (t.Length == 0 || t == "dmy" || t == "ddmmyyyy" || t == "day" || t == "dayfirst") return MidpDateOrder.DayFirst;
            if (t == "mdy" || t == "mmddyyyy" || t == "month" || t == "monthfirst") return MidpDateOrder.MonthFirst;
            return null;
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
