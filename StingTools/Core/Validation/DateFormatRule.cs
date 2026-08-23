using System;
using System.Collections.Generic;

namespace StingTools.Core.Validation
{
    /// <summary>
    /// Whether a date-typed asset parameter holds a well-formed ISO date.
    ///
    /// WHY THIS EXISTS
    /// Every date parameter in the registry is TEXT. The BEP mandates YYYY-MM-DD
    /// and nothing enforced it, so three conventions can coexist unnoticed --
    /// "12/03/2027", "12 Mar 2027" and "2027-03-12" all look fine in a schedule.
    /// It surfaces at close-out, when the handover dataset is assembled and the
    /// dates cannot be sorted or compared, by which point the installer has left
    /// site and the value cannot be recovered.
    ///
    /// The check is on the READ side rather than the write side deliberately.
    /// This data is captured by the Contractor and arrives through COBie import
    /// and spreadsheet round-trips, not by someone typing into a plugin dialog,
    /// so a SetDate/GetDate helper would only guard the one path that is already
    /// correct. Validating what is present catches the value however it arrived.
    ///
    /// SCOPE IS AN ALLOW-LIST, NOT A NAME PATTERN
    /// Matching every *_DATE_TXT would be wrong. Four title-block parameters are
    /// documented as DD-Mon-YYYY on purpose (PRJ_TB_DATE_APVD_TXT,
    /// PRJ_TB_DATE_CHECKED_TXT, PRJ_TB_DATE_DRAWN_TXT, PRJ_TB_REVISION_DATE_TXT)
    /// and flagging them would report correct data as broken -- the same failure
    /// as requiring a parameter on a category it is not bound to: every element
    /// fails and there is no remedy. So only parameters whose registry entry
    /// states ISO 8601 / YYYY-MM-DD are listed here.
    ///
    /// Revit-free by design so it can be exercised by StingTools.Tags.Tests.
    /// </summary>
    public static class DateFormatRule
    {
        /// <summary>The format every parameter below is required to hold.</summary>
        public const string Format = "YYYY-MM-DD";

        /// <summary>
        /// Asset parameters whose registry description states ISO 8601 or
        /// YYYY-MM-DD. Deliberately excludes the DD-Mon-YYYY title-block dates.
        ///
        /// MNT_WARRANTY_EXPIRY_TXT is included even though the corporate registry
        /// describes it as "date or period": BEP section 14.3 records expiry as a
        /// date and captures the period separately in the warranty duration
        /// field, precisely so the value that triggers action is comparable.
        /// </summary>
        public static readonly HashSet<string> IsoDateParams =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ASS_INSTALLATION_DATE_TXT",
                "ASS_INST_DATE_TXT",          // deprecated alias, still bound in live models
                "COM_INSTALL_DATE_TXT",       // legacy COBie alias of the canonical parameter
                "COMM_DATE_TXT",
                "COM_COMMISSION_DATE_TXT",
                "MNT_WARRANTY_EXPIRY_TXT",
                "MNT_LAST_SERVICE_DATE_TXT",
                "MNT_NEXT_SERVICE_DATE_TXT",
                "ASS_CONDITION_DATE_TXT",
                "ASS_SHIP_DATE_TXT",
                "RGL_INSPECTION_DATE_TXT",
                "SLV_INSPECTION_DATE_TXT",
                "ASBUILT_CAPTURE_DATE_TXT",
                "PEN_INSTALL_DATE",
                "PEN_INSPECTION_DATE",
            };

        /// <summary>True if this parameter is required to hold an ISO date.</summary>
        public static bool IsDateParam(string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return false;
            return IsoDateParams.Contains(paramName.Trim());
        }

        /// <summary>
        /// True if <paramref name="value"/> is a well-formed YYYY-MM-DD date.
        ///
        /// An EMPTY value is conforming here. Absence is a completeness question
        /// and the caller already answers it; reporting one missing value as both
        /// missing and malformed would double-count the same defect and inflate
        /// the non-conformance figure the monthly report is read for.
        ///
        /// The calendar is checked, not just the shape: 2027-02-30 matches the
        /// pattern and is not a date, and a value that cannot be parsed is
        /// exactly as unusable at handover as one in the wrong format.
        /// </summary>
        public static bool IsConforming(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;

            string v = value.Trim();
            if (v.Length != 10 || v[4] != '-' || v[7] != '-') return false;

            int year, month, day;
            if (!TryDigits(v, 0, 4, out year)) return false;
            if (!TryDigits(v, 5, 2, out month)) return false;
            if (!TryDigits(v, 8, 2, out day)) return false;

            if (month < 1 || month > 12) return false;
            if (year < 1 || year > 9999) return false;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            return true;
        }

        /// <summary>
        /// A one-line explanation of why a value was rejected, for a report a
        /// person has to act on. Returns null when the value conforms.
        /// </summary>
        public static string Explain(string paramName, string value)
        {
            if (IsConforming(value)) return null;
            return string.Format(
                "{0} holds '{1}', which is not {2}. Dates are held as text, so a mixed " +
                "convention is only discovered when the handover dataset is assembled and " +
                "the dates cannot be sorted or compared.",
                paramName, (value ?? string.Empty).Trim(), Format);
        }

        private static bool TryDigits(string s, int start, int count, out int value)
        {
            value = 0;
            for (int i = start; i < start + count; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
            }
            return true;
        }
    }
}
