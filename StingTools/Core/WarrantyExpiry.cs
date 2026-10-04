// ══════════════════════════════════════════════════════════════════════════
//  WarrantyExpiry.cs — what a warranty expiry IS, from what the element says.
//
//  KUT deep review MEP-3. The Warranty Tracker used to write
//  MNT_WARRANTY_EXPIRY_TXT = today + a per-category default (5 / 3 / 2 / 5 / 10
//  years) onto every empty element, and ASS_WARRANTY_TXT = "N years". Both were
//  invented, and the invented expiry then satisfied the KUT LOD-500 rung that
//  asks for one. An expiry is now computed only from a start date and a duration
//  that are both recorded on the element; anything else is reported as missing.
//
//  Revit-free (unit-tested in StingTools.Boq.Tests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Globalization;

namespace StingTools.Core
{
    public enum WarrantyState
    {
        /// <summary>An expiry is already recorded; it is kept.</summary>
        Recorded,
        /// <summary>Expiry computed from the recorded start date + duration.</summary>
        Computed,
        /// <summary>No warranty start / installation date on the element.</summary>
        MissingStart,
        /// <summary>No warranty duration on the element.</summary>
        MissingDuration,
        /// <summary>A recorded value could not be read (named in Detail).</summary>
        Unreadable,
    }

    public sealed class WarrantyPlan
    {
        public WarrantyState State;
        public string Expiry = "";      // yyyy-MM-dd, or "" when unknown
        public string Start = "";       // yyyy-MM-dd, or ""
        public bool? Expired;           // null when the expiry is unknown
        public string Detail = "";

        /// <summary>True only when the tracker should write MNT_WARRANTY_EXPIRY_TXT.</summary>
        public bool ShouldWrite => State == WarrantyState.Computed;
    }

    public static class WarrantyExpiry
    {
        private static readonly string[] DateFormats =
            { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ" };

        public static bool TryParseDate(string text, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date);
        }

        /// <summary>Duration in months from a number + unit ("years" default, "months").</summary>
        public static bool TryParseDurationMonths(string amount, string unit, out int months)
        {
            months = 0;
            if (string.IsNullOrWhiteSpace(amount)) return false;
            string a = amount.Trim();
            // tolerate a trailing unit word ("5 years", "18 months")
            string u = (unit ?? "").Trim().ToLowerInvariant();
            var parts = a.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) { a = parts[0]; if (u.Length == 0) u = parts[1].Trim().ToLowerInvariant(); }
            if (!double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0 || v > 100)
                return false;
            bool isMonths = u.StartsWith("month") || u == "m" || u == "mo";
            double m = isMonths ? v : v * 12.0;
            if (Math.Abs(m - Math.Round(m)) > 1e-6) return false;   // a fraction of a month is not a duration anyone wrote
            months = (int)Math.Round(m);
            return months > 0;
        }

        /// <param name="recordedExpiry">MNT_WARRANTY_EXPIRY_TXT as found.</param>
        /// <param name="start">Warranty start, else installation date — the caller picks the first non-empty.</param>
        /// <param name="durationAmount">Warranty duration as recorded (parts, else labour).</param>
        /// <param name="durationUnit">ASS_WARRANTY_DUR_UNIT_TXT; empty = years.</param>
        /// <param name="today">Injected for tests.</param>
        public static WarrantyPlan Plan(string recordedExpiry, string start, string durationAmount,
                                        string durationUnit, DateTime today)
        {
            var p = new WarrantyPlan();
            if (!string.IsNullOrWhiteSpace(recordedExpiry))
            {
                if (!TryParseDate(recordedExpiry, out var rec))
                    return new WarrantyPlan { State = WarrantyState.Unreadable, Detail = $"expiry '{recordedExpiry}' is not yyyy-MM-dd" };
                p.State = WarrantyState.Recorded;
                p.Expiry = rec.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                p.Expired = rec.Date < today.Date;
                return p;
            }
            if (string.IsNullOrWhiteSpace(start))
                return new WarrantyPlan { State = WarrantyState.MissingStart, Detail = "no warranty start or installation date" };
            if (!TryParseDate(start, out var s))
                return new WarrantyPlan { State = WarrantyState.Unreadable, Detail = $"start '{start}' is not yyyy-MM-dd" };
            p.Start = s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(durationAmount))
            {
                p.State = WarrantyState.MissingDuration;
                p.Detail = "no warranty duration";
                return p;
            }
            if (!TryParseDurationMonths(durationAmount, durationUnit, out int months))
            {
                p.State = WarrantyState.Unreadable;
                p.Detail = $"duration '{durationAmount}' is not a number of years or months";
                return p;
            }
            var exp = s.AddMonths(months);
            p.State = WarrantyState.Computed;
            p.Expiry = exp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            p.Expired = exp.Date < today.Date;
            return p;
        }
    }
}
