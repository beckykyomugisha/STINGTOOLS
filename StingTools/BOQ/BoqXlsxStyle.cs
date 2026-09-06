// ══════════════════════════════════════════════════════════════════════════
//  BoqXlsxStyle.cs — shared ClosedXML styling for every BOQ-family workbook.
//
//  Extracted from BOQExportCommand (where these were private instance methods)
//  so the material-schedule renderer produces visually identical output instead
//  of a near-miss duplicate. BOQExportCommand now delegates here; its own
//  private wrappers remain so its ~40 call sites are untouched.
//
//  The bodies are copied VERBATIM from BOQExportCommand — the banner really does
//  merge columns 1..16 at font size 12, and the header really does not wrap.
//  Producing byte-identical BOQ workbooks matters more than tidying either one.
// ══════════════════════════════════════════════════════════════════════════
using ClosedXML.Excel;

namespace StingTools.BOQ
{
    internal static class BoqXlsxStyle
    {
        public static readonly XLColor NavyFill   = XLColor.FromArgb(26, 58, 92);
        public static readonly XLColor HeaderFill = XLColor.FromArgb(46, 94, 142);
        public static readonly XLColor ManualRow  = XLColor.FromArgb(255, 251, 230);

        /// <summary>Full-width navy title banner on row 1.</summary>
        public static void BannerRow(IXLWorksheet ws, string text)
        {
            ws.Cell(1, 1).Value = text;
            ws.Range(1, 1, 1, 16).Merge().Style.Font.SetBold().Font.SetFontSize(12)
                .Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(NavyFill);
        }

        /// <summary>Bold white-on-blue column header row.</summary>
        public static void WriteHeader(IXLWorksheet ws, int row, string[] cols)
        {
            for (int i = 0; i < cols.Length; i++) ws.Cell(row, i + 1).Value = cols[i];
            ws.Range(row, 1, row, cols.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(HeaderFill);
        }

        /// <summary>UGX money format — thousands separated, no decimals.</summary>
        public static void MoneyFormat(IXLRange range)
        {
            range.Style.NumberFormat.Format = "#,##0";
        }
    }
}
