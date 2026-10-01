// StingTools — Drawing Self-Test (DTW-82), the Revit-free half
//
// DrawingTypes_SelfTest runs the drawing layer's in-Revit checks against the open
// model inside a TransactionGroup it always rolls back. What it checks needs Revit;
// what it reports does not, so the result rows, the CSV, the well-formedness rules
// and the binding lists live here, where StingTools.Tags.Tests can test them.
//
// The binding lists mirror tools/drawing_binding_contract.json, which is not shipped
// with the plugin (it lives under tools/, read by the CI gate). The mirror is held to
// the file by DrawingSelfTestModelTests: edit one and that test fails until the other
// says the same.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public enum SelfTestStatus
    {
        Pass,
        Fail,
        /// <summary>Not run — the model lacks what the check needs. Always has a reason.</summary>
        Skip,
        /// <summary>An observation answering an open design question; neither pass nor fail.</summary>
        Info,
    }

    public sealed class SelfTestRow
    {
        public SelfTestRow(string check, string item, SelfTestStatus status, string detail)
        {
            Check = check ?? "";
            Item = item ?? "";
            Status = status;
            Detail = detail ?? "";
        }

        /// <summary>The check's letter and name, e.g. "a. Bindings".</summary>
        public string Check { get; }
        /// <summary>What was checked within it, e.g. the parameter name.</summary>
        public string Item { get; }
        public SelfTestStatus Status { get; }
        public string Detail { get; }
    }

    public static class DrawingSelfTestModel
    {
        /// <summary>The report's first line: what the run did to the model.</summary>
        public const string RollbackHeader =
            "Every change this test made was rolled back (one TransactionGroup, RollBack at the end): "
            + "the model is as it was. Not rolled back: the STING log, this CSV, and in a workshared "
            + "model any element Revit borrowed, which stays borrowed until you relinquish or sync.";

        // ── tools/drawing_binding_contract.json, mirrored ────────────────────
        // Held equal to the file by DrawingSelfTestModelTests.

        public static readonly IReadOnlyList<string> ViewParams = new[]
        {
            "STING_BUS_VOLTAGE_TIER",
            "STING_CIRCUIT_FILTER_TXT",
            "STING_CROP_KIND_TXT",
            "STING_CROP_MARGIN_MM_TXT",
            "STING_DEFAULT_TAG_STYLE_TXT",
            "STING_DRAWING_PACKAGE_ID_TXT",
            "STING_DRAWING_TYPE_ID_TXT",
            "STING_PACK_CHECKSUM_TXT",
            "STING_PACK_ID_TXT",
            "STING_PRODUCTION_RULE_IDX_INT",
            "STING_SCOPE_BOX_TAG_TXT",
            "STING_STYLE_LOCKED_BOOL",
            "STING_TEMPLATE_CHECKSUM_TXT",
            "STING_TEMPLATE_LOCKED_BOOL",
            "STING_VIEW_CONTEXT_TAG_TXT",
            "STING_VIEW_SYMBOL_STANDARD",
            "STING_VIEW_TAG_STYLE",
            "STING_VIEW_TOKEN_MASK_TXT",
        };

        public static readonly IReadOnlyList<string> SheetParams = new[]
        {
            "PRJ_SHEET_SEQUENCE_INT",
            "STING_DRAWING_PACKAGE_ID_TXT",
            "STING_DRAWING_TYPE_ID_TXT",
            "STING_SHEET_CONTEXT_TXT",
            "STING_STYLE_LOCKED_BOOL",
        };

        public static readonly IReadOnlyList<string> LineParams = new[]
        {
            "STING_MATCH_DIR_TXT",
            "STING_MATCH_LINE_GUID_TXT",
            "STING_MATCH_REF_TXT",
        };

        /// <summary>Bound to Project Information (the title block's BIM / non-BIM switch).</summary>
        public const string ProjectInfoParam = "PRJ_SHEET_BIM_MODE_TXT";

        /// <summary>
        /// The five AEC filters the test creates: one per kind of rule that has been in
        /// doubt (phase, structural material, healthcare text, MEP system, electrical).
        /// </summary>
        public static readonly IReadOnlyList<string> RepresentativeFilters = new[]
        {
            "clin-press-negative",
            "phase-demolished",
            "struct-concrete",
            "hvac-supply-air",
            "elec-small-power",
        };

        // ── Well-formedness ──────────────────────────────────────────────────

        /// <summary>
        /// A sheet number a person could issue: not empty, every token substituted,
        /// no empty segment ("A--001", "-001", "A-"), no whitespace at the ends.
        /// </summary>
        public static bool IsWellFormedSheetNumber(string number, out string why)
        {
            why = null;
            if (string.IsNullOrWhiteSpace(number)) { why = "empty"; return false; }
            if (number != number.Trim()) { why = "leading or trailing whitespace"; return false; }
            if (number.IndexOf('{') >= 0 || number.IndexOf('}') >= 0) { why = "an unsubstituted {token}"; return false; }
            if (number.Contains("--")) { why = "an empty segment (--)"; return false; }
            if (number.StartsWith("-", StringComparison.Ordinal) || number.EndsWith("-", StringComparison.Ordinal))
            { why = "starts or ends with a separator"; return false; }
            return true;
        }

        private static readonly Regex _isoLevel = new Regex(@"^[A-Z0-9]{2,3}$", RegexOptions.CultureInvariant);

        /// <summary>An ISO 19650 level code: two or three upper-case letters or digits (00, 01, B1, XX, ZZ, RF, M01).</summary>
        public static bool IsWellFormedLevelCode(string code)
            => !string.IsNullOrEmpty(code) && _isoLevel.IsMatch(code);

        /// <summary>
        /// Does a schedule's top-left corner (x, y) land inside the slot [left..right] x
        /// [bottom..top]? All in the same unit; <paramref name="tol"/> absorbs rounding.
        /// </summary>
        public static bool TopLeftInside(double x, double y, double left, double right, double bottom, double top, double tol)
            => x >= left - tol && x <= right + tol && y >= bottom - tol && y <= top + tol;

        // ── Report ───────────────────────────────────────────────────────────

        public static int Count(IEnumerable<SelfTestRow> rows, SelfTestStatus s)
            => rows?.Count(r => r.Status == s) ?? 0;

        /// <summary>"12 pass, 1 fail, 3 skip, 2 info".</summary>
        public static string Summary(IReadOnlyCollection<SelfTestRow> rows)
            => $"{Count(rows, SelfTestStatus.Pass)} pass, {Count(rows, SelfTestStatus.Fail)} fail, "
             + $"{Count(rows, SelfTestStatus.Skip)} skip, {Count(rows, SelfTestStatus.Info)} info";

        public static string StatusText(SelfTestStatus s)
        {
            switch (s)
            {
                case SelfTestStatus.Pass: return "PASS";
                case SelfTestStatus.Fail: return "FAIL";
                case SelfTestStatus.Skip: return "SKIP";
                default: return "INFO";
            }
        }

        /// <summary>The CSV: a header comment line, then one row per result.</summary>
        public static string ToCsv(IEnumerable<SelfTestRow> rows, string modelTitle, DateTime when)
        {
            var sb = new StringBuilder();
            sb.Append("# ").AppendLine(Csv($"STING Drawing Self-Test — {modelTitle} — {when:yyyy-MM-dd HH:mm:ss}"));
            sb.Append("# ").AppendLine(Csv(RollbackHeader));
            sb.AppendLine("Check,Item,Status,Detail");
            foreach (var r in rows ?? Enumerable.Empty<SelfTestRow>())
                sb.Append(Csv(r.Check)).Append(',')
                  .Append(Csv(r.Item)).Append(',')
                  .Append(StatusText(r.Status)).Append(',')
                  .AppendLine(Csv(r.Detail));
            return sb.ToString();
        }

        /// <summary>RFC 4180 quoting: a field with a comma, quote or line break is quoted, quotes doubled.</summary>
        public static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
