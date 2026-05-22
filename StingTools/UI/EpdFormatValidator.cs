using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// A4 — EPD source format validator.
    ///
    /// Recognises the common EPD identifier conventions: EC3-#####,
    /// EPD-XX-####, EPDIE-#####, IBU-####, DAPCONS-####, NMD-####,
    /// ECO-PLATFORM-#####. Free-text outside these patterns is flagged
    /// as "informal" — accepted but not certifiable.
    /// </summary>
    public enum EpdFormatVerdict { Empty, Valid, InformalText, BadShape }

    public static class EpdFormatValidator
    {
        private static readonly Regex _ec3        = new Regex(@"^EC3[-_ ]?\d{4,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _epdInt     = new Regex(@"^EPD[-_ ]?[A-Z]{2,4}[-_ ]?\d{4,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _ibu        = new Regex(@"^IBU[-_ ]?\d{3,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _dapcons    = new Regex(@"^DAPCONS[-_ ]?\d{3,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _nmd        = new Regex(@"^NMD[-_ ]?\d{3,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _ecoPlatfrm = new Regex(@"^ECO[-_ ]?PLATFORM[-_ ]?\d{4,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _epdRef     = new Regex(@"^EPD[-_ ]?\d{4,}$", RegexOptions.IgnoreCase);
        private static readonly Regex _looksLikeId = new Regex(@"^[A-Z0-9][A-Z0-9\-_ ]{3,20}$", RegexOptions.IgnoreCase);

        public static EpdFormatVerdict Validate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return EpdFormatVerdict.Empty;
            string s = raw.Trim();
            if (_ec3.IsMatch(s) ||
                _epdInt.IsMatch(s) ||
                _ibu.IsMatch(s) ||
                _dapcons.IsMatch(s) ||
                _nmd.IsMatch(s) ||
                _ecoPlatfrm.IsMatch(s) ||
                _epdRef.IsMatch(s))
                return EpdFormatVerdict.Valid;
            // Free-text that vaguely looks like an id: "ICE-data" or
            // "manufacturer-EPD". Accept but warn.
            if (_looksLikeId.IsMatch(s)) return EpdFormatVerdict.InformalText;
            return EpdFormatVerdict.BadShape;
        }

        /// <summary>
        /// One-shot scan over every project Material's
        /// STING_MAT_EPD_SRC_TXT. Returns a per-row verdict report.
        /// </summary>
        public static System.Collections.Generic.List<(string materialName, string raw, EpdFormatVerdict verdict)>
            ScanProject(Document doc)
        {
            var rows = new System.Collections.Generic.List<(string, string, EpdFormatVerdict)>();
            if (doc == null) return rows;
            try
            {
                foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material))
                                     .Cast<Material>())
                {
                    try
                    {
                        var p = m.LookupParameter("STING_MAT_EPD_SRC_TXT");
                        string raw = (p != null && p.HasValue && p.StorageType == StorageType.String) ? p.AsString() : "";
                        rows.Add((m.Name ?? "", raw ?? "", Validate(raw)));
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("Epd.Scan", $"EPD scan '{m?.Name}': {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"EpdFormatValidator.ScanProject: {ex.Message}"); }
            return rows;
        }
    }
}
