// ══════════════════════════════════════════════════════════════════════════
//  CommissioningDate.cs — what COMM_DATE_TXT holds, and when it is written.
//
//  KUT deep review MEP-5. QR commissioning wrote COMM_DATE_TXT as
//  yyyy-MM-ddTHH:mm:ssZ on EVERY state change. Two faults: the LOD / owner date
//  rule (DateFormatRule) requires yyyy-MM-dd, so every Tier A item commissioned
//  through STING's own workflow failed the rung that asks for the date; and the
//  value recorded the last state change (RECEIVED, INSTALLED, …), not the date
//  the asset was commissioned. The full timestamp of every transition is still
//  in the QR audit log.
//
//  Revit-free (unit-tested in StingTools.Tags.Tests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Globalization;

namespace StingTools.Core.Validation
{
    public static class CommissioningDate
    {
        public const string Commissioned = "COMMISSIONED";
        public const string Handover = "HANDOVER";

        /// <summary>
        /// The COMM_DATE_TXT value to write for a transition to <paramref name="targetState"/>,
        /// or null to leave the parameter as it is.
        ///   COMMISSIONED  → today (UTC) as yyyy-MM-dd — a re-commissioning updates it.
        ///   any other     → unchanged, except an old timestamp written by the previous
        ///                   code is cut to its own date (same day, nothing invented).
        /// </summary>
        public static string ValueFor(string targetState, string existing, DateTime utcNow)
        {
            if (string.Equals(targetState?.Trim(), Commissioned, StringComparison.OrdinalIgnoreCase))
                return utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string e = existing?.Trim() ?? "";
            if (e.Length > 10 && e[10] == 'T' && DateFormatRule.IsConforming(e.Substring(0, 10)))
                return e.Substring(0, 10);
            return null;
        }
    }
}
