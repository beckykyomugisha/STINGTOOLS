// ══════════════════════════════════════════════════════════════════════════
//  BOQProfessionalExportCommand.cs — Phase 108g
//  Generates a tender-grade Bill of Quantities XLSX following UK NRM2 /
//  RICS conventions used by senior quantity surveyors. Sheets:
//
//    1  Cover             — Corporate cover page (employer/project/QS firm)
//    2  Document Control  — Revision history table
//    3  Contents          — Table of contents with section refs
//    4  Preliminaries     — NRM2 Part 1 general preliminaries (tokenised)
//    5  Preambles         — Per-discipline materials & workmanship preambles
//   6+  Bill Section X.Y  — One sheet per BOQ section with carry-forward
//        "To Collection" footer. Columns: Ref / Description / Unit / Qty /
//        Rate / Amount — industry-standard QS layout.
//    N  Collections       — Sub-totals of each bill section grouped under
//        discipline headings, collection-of-collection structure.
//    N  Grand Summary     — All section totals + preliminaries +
//        provisional sums + contingency + OH&P + VAT → Contract Sum
//    N  PC / PS / Dayworks — Annexure with prime cost sums, provisional
//        sums and dayworks schedule for the contract.
//
//  Presentation conventions:
//  - Times New Roman 11 body, Arial 10 for section headers, Arial Bold
//    14 for sheet titles — matches the Millwood / Gardiner & Theobald
//    house styles commonly used on UK tenders.
//  - A4 portrait for preliminaries / cover, A4 landscape for bills.
//  - Frozen headers, repeating header rows on print.
//  - Figures right-aligned to two decimals, zero rates shown as "-".
//  - Unit column uses Unicode symbols (m², m³) not "m2"/"m3".
//  - Grand Summary contract sum framed in double borders per QS norm.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQProfessionalExportCommand : IExternalCommand
    {
        // ── Corporate palette ──────────────────────────────────────────────
        private static readonly XLColor Navy = XLColor.FromArgb(26, 58, 92);
        private static readonly XLColor NavyDeep = XLColor.FromArgb(14, 36, 63);
        private static readonly XLColor Orange = XLColor.FromArgb(232, 145, 45);
        private static readonly XLColor GreyLight = XLColor.FromArgb(242, 244, 247);
        private static readonly XLColor GreyMid = XLColor.FromArgb(212, 218, 226);
        private static readonly XLColor White = XLColor.White;

        // Font families
        private const string BodyFont = "Times New Roman";
        private const string HeadFont = "Arial";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq == null || boq.Sections.Count == 0)
                {
                    TaskDialog.Show("Professional BOQ", "No BOQ items to export.");
                    return Result.Cancelled;
                }

                // Pre-export — guarantee every line has a clean paragraph so
                // bill descriptions never ship with [tokens] or blanks.
                EnsureAllParagraphsResolved(boq, doc);

                // Write cost parameters back to elements so the model + the
                // workbook agree, then produce the workbook.
                using (var tx = new Transaction(doc, "STING BOQ — professional export"))
                {
                    tx.Start();
                    BOQCostManager.WriteElementParameters(doc, boq.AllItems);
                    BOQCostManager.WriteProjectParameters(doc, boq);
                    tx.Commit();
                }

                var meta = BuildProjectMetadata(doc, boq);
                string outputPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_BOQ_Professional", ".xlsx");

                using (var wb = new XLWorkbook())
                {
                    // Workbook-level properties for Windows "Details" pane
                    wb.Properties.Title = $"Bill of Quantities — {meta.ProjectName}";
                    wb.Properties.Author = meta.QsFirm;
                    wb.Properties.Company = meta.Employer;
                    wb.Properties.Subject = "Bill of Quantities (NRM2)";
                    wb.Properties.Keywords = "BOQ, NRM2, Tender, Contract";

                    int billSheetIndex = 0;
                    var tocRows = new List<(string Code, string Name, string SheetName)>();

                    BuildCoverSheet(wb.Worksheets.Add("Cover"), meta);
                    BuildDocumentControlSheet(wb.Worksheets.Add("Document Control"), meta);
                    BuildPreliminariesSheet(wb.Worksheets.Add("Preliminaries"), meta);
                    BuildPreamblesSheet(wb.Worksheets.Add("Preambles"), boq);

                    // Bills — one sheet per section; record entries for the
                    // Contents and Collections sheets as we go.
                    var sectionSheetNames = new Dictionary<string, string>();
                    var sectionTotals = new List<(BOQSection Sec, string SheetName, double TotalUGX)>();
                    foreach (var sec in boq.Sections)
                    {
                        billSheetIndex++;
                        string sheetName = SafeSheetName($"{billSheetIndex:00} {sec.NRM2Section} {sec.Name}");
                        sectionSheetNames[sec.Id] = sheetName;
                        var ws = wb.Worksheets.Add(sheetName);
                        double total = BuildBillSheet(ws, sec, meta);
                        sectionTotals.Add((sec, sheetName, total));
                        tocRows.Add((sec.NRM2Section, sec.Name, sheetName));
                    }

                    BuildCollectionsSheet(wb.Worksheets.Add("Collections"), sectionTotals, meta);
                    BuildGrandSummarySheet(wb.Worksheets.Add("Grand Summary"), boq, sectionTotals, meta);
                    BuildAnnexureSheet(wb.Worksheets.Add("Annexure — PC & Dayworks"), boq, meta);

                    // Contents sheet last so we know every sheet name, but
                    // insert it after "Document Control" for navigation.
                    var contents = wb.Worksheets.Add("Contents", 3);
                    BuildContentsSheet(contents, tocRows, sectionTotals, meta);

                    wb.SaveAs(outputPath);
                }

                try { Process.Start("explorer.exe", $"/select,\"{outputPath}\""); }
                catch (Exception ex) { StingLog.Warn($"Explorer open: {ex.Message}"); }

                UI.StingResultPanel.Create("Professional BOQ exported")
                    .SetSubtitle($"Tender-grade NRM2 bill of quantities — {boq.AllItems.Count} items across {boq.Sections.Count} sections")
                    .AddSection("FILE")
                    .Text(outputPath)
                    .AddSection("SUMMARY")
                    .Metric("Sections", boq.Sections.Count.ToString())
                    .Metric("Items", boq.AllItems.Count.ToString("N0"))
                    .Metric("Modeled", $"UGX {boq.ModeledTotalUGX:N0}")
                    .Metric("Provisional", $"UGX {boq.ProvTotalUGX:N0}")
                    .Metric("Grand total", $"UGX {boq.GrandTotalUGX:N0}")
                    .Show();

                StingLog.Info($"Professional BOQ exported: {Path.GetFileName(outputPath)} ({boq.Sections.Count} sections, {boq.AllItems.Count} items)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQProfessionalExportCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Professional BOQ", $"Export failed: {ex.Message}");
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Project metadata — read from doc.ProjectInformation + project_config.json
        // ══════════════════════════════════════════════════════════════════

        private class ProjectMeta
        {
            public string Employer;
            public string ProjectName;
            public string ProjectNumber;
            public string ProjectAddress;
            public string QsFirm;
            public string Architect;
            public string Engineer;
            public string ContractorRef;
            public string Stage;
            public string Revision;
            public string RevisionDate;
            public string TenderStatus;
            public string MeasurementStandard;
            public string Currency;
            public double ExchangeRate;
            public double PrelimPct;
            public double ContingencyPct;
            public double OverheadPct;
            public double VatPct;
        }

        private ProjectMeta BuildProjectMetadata(Document doc, BOQDocument boq)
        {
            var pi = doc?.ProjectInformation;
            string piName = pi?.get_Parameter(BuiltInParameter.PROJECT_NAME)?.AsString() ?? pi?.Name ?? doc?.Title ?? "Unknown Project";
            string piNum = pi?.get_Parameter(BuiltInParameter.PROJECT_NUMBER)?.AsString() ?? "";
            string piAddr = pi?.get_Parameter(BuiltInParameter.PROJECT_ADDRESS)?.AsString() ?? "";
            string piClient = pi?.get_Parameter(BuiltInParameter.CLIENT_NAME)?.AsString() ?? "";
            string piOrg = pi?.get_Parameter(BuiltInParameter.PROJECT_ORGANIZATION_NAME)?.AsString() ?? "";

            return new ProjectMeta
            {
                Employer            = Coalesce(TagConfig.GetConfigValue("BOQ_EMPLOYER_NAME") ?? "",    piClient,  "[Employer name]"),
                ProjectName         = Coalesce(TagConfig.GetConfigValue("BOQ_PROJECT_NAME") ?? "",     piName,    "[Project name]"),
                ProjectNumber       = Coalesce(TagConfig.GetConfigValue("BOQ_PROJECT_NUMBER") ?? "",   piNum,     "[Project number]"),
                ProjectAddress      = Coalesce(TagConfig.GetConfigValue("BOQ_PROJECT_ADDRESS") ?? "",  piAddr,    "[Project address]"),
                QsFirm              = Coalesce(TagConfig.GetConfigValue("BOQ_QS_FIRM") ?? "",          piOrg,     "[Quantity Surveyor]"),
                Architect           = Coalesce(TagConfig.GetConfigValue("BOQ_ARCHITECT") ?? "",                   "[Architect]"),
                Engineer            = Coalesce(TagConfig.GetConfigValue("BOQ_ENGINEER") ?? "",                    "[Services Engineer]"),
                ContractorRef       = Coalesce(TagConfig.GetConfigValue("BOQ_CONTRACTOR") ?? "",                  "[Main Contractor — to be appointed]"),
                Stage               = Coalesce(TagConfig.GetConfigValue("BOQ_WORK_STAGE") ?? "",                  "RIBA Stage 4 — Technical Design"),
                Revision            = Coalesce(TagConfig.GetConfigValue("BOQ_REVISION") ?? "",                    "P01"),
                RevisionDate        = DateTime.UtcNow.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                TenderStatus        = Coalesce(TagConfig.GetConfigValue("BOQ_TENDER_STATUS") ?? "",               "FOR TENDER"),
                MeasurementStandard = "RICS New Rules of Measurement (NRM2) — Detailed measurement for building works, 2nd edition",
                Currency            = boq.Currency ?? "UGX",
                ExchangeRate        = boq.ExchangeRateUgxPerUsd > 0 ? boq.ExchangeRateUgxPerUsd : 3700,
                PrelimPct           = boq.PrelimPct,
                ContingencyPct      = boq.ContingencyPct,
                OverheadPct         = boq.OverheadPct,
                VatPct              = TagConfig.GetConfigDouble("BOQ_VAT_PCT", 18.0),
            };
        }

        private static string Coalesce(params string[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        // ══════════════════════════════════════════════════════════════════
        //  Sheet builders
        // ══════════════════════════════════════════════════════════════════

        private void BuildCoverSheet(IXLWorksheet ws, ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.Margins.Top = 0.6; ws.PageSetup.Margins.Bottom = 0.6;
            ws.PageSetup.Margins.Left = 0.7; ws.PageSetup.Margins.Right = 0.7;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 90;

            // Top navy band — corporate letterhead strip
            ws.Range("A1:C3").Merge().Style
                .Fill.SetBackgroundColor(Navy);
            ws.Cell("B2").Value = m.QsFirm;
            ws.Cell("B2").Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(18).Font.SetBold(true)
                .Font.SetFontColor(White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            // Orange accent rule
            ws.Range("A4:C4").Merge().Style.Fill.SetBackgroundColor(Orange);
            ws.Row(4).Height = 3;

            // Document type
            ws.Cell("B7").Value = "BILL OF QUANTITIES";
            ws.Cell("B7").Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(36).Font.SetBold(true)
                .Font.SetFontColor(Navy)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ws.Cell("B9").Value = m.MeasurementStandard;
            ws.Cell("B9").Style
                .Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(90, 90, 90))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Separator
            ws.Range("B11:B11").Style.Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(Navy);

            // Project block — centered
            ws.Cell("B14").Value = "PROJECT";
            ws.Cell("B14").Style.Font.SetFontName(HeadFont).Font.SetFontSize(9).Font.SetBold(true)
                .Font.SetFontColor(XLColor.FromArgb(110, 110, 110))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ws.Cell("B15").Value = m.ProjectName;
            ws.Cell("B15").Style.Font.SetFontName(HeadFont).Font.SetFontSize(22).Font.SetBold(true)
                .Font.SetFontColor(Navy)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            if (!string.IsNullOrWhiteSpace(m.ProjectAddress))
            {
                ws.Cell("B16").Value = m.ProjectAddress;
                ws.Cell("B16").Style.Font.SetFontName(BodyFont).Font.SetFontSize(11).Font.SetItalic(true)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }

            // Key-value block — employer, QS, architect, engineer
            var meta = new[]
            {
                ("Employer / Client",       m.Employer),
                ("Project Number",          m.ProjectNumber),
                ("Work Stage",              m.Stage),
                ("Quantity Surveyor",       m.QsFirm),
                ("Architect",               m.Architect),
                ("Services Engineer",       m.Engineer),
                ("Contractor",              m.ContractorRef),
            };
            int row = 19;
            foreach (var (label, value) in meta)
            {
                ws.Cell(row, 2).Value = label.ToUpperInvariant() + "   " + value;
                var lbl = ws.Cell(row, 2);
                lbl.RichText.ClearText();
                lbl.RichText.AddText(label.ToUpperInvariant() + "   ")
                    .SetFontName(HeadFont).SetFontSize(9).SetBold(true).SetFontColor(XLColor.FromArgb(110, 110, 110));
                lbl.RichText.AddText(value)
                    .SetFontName(HeadFont).SetFontSize(12).SetFontColor(Navy);
                lbl.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                row++;
            }

            // Revision strip (bottom)
            row = 40;
            ws.Range(row, 2, row, 2).Style.Border.SetTopBorder(XLBorderStyleValues.Thin)
                .Border.SetTopBorderColor(Navy);
            row++;
            ws.Cell(row, 2).Value = $"Revision {m.Revision}   ·   {m.RevisionDate}   ·   {m.TenderStatus}";
            ws.Cell(row, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                .Font.SetFontColor(Navy)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            row += 2;
            ws.Cell(row, 2).Value = $"© {DateTime.Now.Year} {m.QsFirm}. This document is issued to the addressee for tender purposes only "
                + "and must not be reproduced, copied or transmitted in whole or in part without the express written consent "
                + "of the Quantity Surveyor. All measurements are in accordance with NRM2.";
            ws.Cell(row, 2).Style.Font.SetFontName(BodyFont).Font.SetFontSize(8)
                .Font.SetFontColor(XLColor.FromArgb(130, 130, 130))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetWrapText(true);
            ws.Row(row).Height = 36;
        }

        private void BuildDocumentControlSheet(IXLWorksheet ws, ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;  // Rev
            ws.Column(3).Width = 14;  // Date
            ws.Column(4).Width = 42;  // Description of change
            ws.Column(5).Width = 14;  // Author
            ws.Column(6).Width = 12;  // Checked
            ws.Column(7).Width = 12;  // Approved

            SheetBanner(ws, "DOCUMENT CONTROL", m);

            int row = 4;
            ws.Cell(row, 2).Value = "REVISION HISTORY";
            ws.Range(row, 2, row, 7).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(row).Height = 22;
            row += 2;

            string[] headers = { "Rev", "Date", "Description of change", "Author", "Checked", "Approved" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(row, 2 + i);
                c.Value = headers[i];
                c.Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Font.SetFontColor(Navy)
                    .Fill.SetBackgroundColor(GreyLight)
                    .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Navy)
                    .Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(GreyMid)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                    .Alignment.SetIndent(1)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            ws.Row(row).Height = 20;
            row++;

            // Current revision from config + placeholder earlier revisions
            var revs = new List<(string R, string D, string Desc, string A, string C, string Ap)>
            {
                (m.Revision, m.RevisionDate, "Issued " + m.TenderStatus.ToLower(CultureInfo.InvariantCulture), m.QsFirm, "", ""),
                ("P00",      "",             "Draft for internal review",                                       m.QsFirm, "", ""),
            };
            foreach (var (R, D, Desc, A, C, Ap) in revs)
            {
                ws.Cell(row, 2).Value = R;
                ws.Cell(row, 3).Value = D;
                ws.Cell(row, 4).Value = Desc;
                ws.Cell(row, 5).Value = A;
                ws.Cell(row, 6).Value = C;
                ws.Cell(row, 7).Value = Ap;
                for (int i = 2; i <= 7; i++)
                {
                    ws.Cell(row, i).Style
                        .Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(GreyMid)
                        .Alignment.SetIndent(1)
                        .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                        .Alignment.SetWrapText(true);
                }
                ws.Row(row).Height = 20;
                row++;
            }

            row += 2;
            ws.Cell(row, 2).Value = "DISTRIBUTION";
            ws.Range(row, 2, row, 7).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(row).Height = 22;
            row += 2;

            var dist = new[]
            {
                ("Employer",  m.Employer),
                ("Architect", m.Architect),
                ("Engineer",  m.Engineer),
                ("Contractor / Bidders", m.ContractorRef),
                ("QS file",   m.QsFirm),
            };
            foreach (var (Role, Who) in dist)
            {
                ws.Cell(row, 2).Value = Role;
                ws.Range(row, 2, row, 3).Merge().Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Alignment.SetIndent(1);
                ws.Cell(row, 4).Value = Who;
                ws.Range(row, 4, row, 7).Merge().Style.Font.SetFontName(BodyFont).Font.SetFontSize(10);
                ws.Row(row).Height = 18;
                row++;
            }
        }

        private void BuildContentsSheet(IXLWorksheet ws,
            List<(string Code, string Name, string SheetName)> tocRows,
            List<(BOQSection Sec, string SheetName, double TotalUGX)> sectionTotals,
            ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 6;    // Item
            ws.Column(3).Width = 54;   // Section name
            ws.Column(4).Width = 18;   // Sheet reference

            SheetBanner(ws, "TABLE OF CONTENTS", m);

            int row = 5;
            string[] pre = { "1", "2", "3", "4" };
            string[] preLbl = { "Document Control", "Preliminaries", "Preambles to Trades", "Bills of Quantities" };
            string[] preSheet = { "Document Control", "Preliminaries", "Preambles", "(see below)" };

            // PART A — prelims / preambles
            ContentsHeader(ws, ref row, "PART A — GENERAL");
            for (int i = 0; i < pre.Length; i++)
                ContentsRow(ws, ref row, pre[i], preLbl[i], preSheet[i]);

            row++;
            ContentsHeader(ws, ref row, "PART B — BILLS OF QUANTITIES");
            int n = 1;
            foreach (var (code, name, sheet) in tocRows)
            {
                ContentsRow(ws, ref row, $"B.{n++}", $"Section {code} — {name}", sheet);
            }

            row++;
            ContentsHeader(ws, ref row, "PART C — SUMMARIES & ANNEXURES");
            ContentsRow(ws, ref row, "C.1", "Collections",  "Collections");
            ContentsRow(ws, ref row, "C.2", "Grand Summary", "Grand Summary");
            ContentsRow(ws, ref row, "C.3", "Annexure — Prime Cost & Dayworks", "Annexure — PC & Dayworks");
        }

        private void ContentsHeader(IXLWorksheet ws, ref int row, string text)
        {
            ws.Cell(row, 2).Value = text;
            ws.Range(row, 2, row, 4).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(row).Height = 22;
            row += 2;
        }

        private void ContentsRow(IXLWorksheet ws, ref int row, string itemRef, string name, string sheetName)
        {
            ws.Cell(row, 2).Value = itemRef;
            ws.Cell(row, 3).Value = name;
            ws.Cell(row, 4).Value = sheetName;
            for (int i = 2; i <= 4; i++)
            {
                ws.Cell(row, i).Style
                    .Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                    .Border.SetBottomBorderColor(GreyMid)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }
            ws.Cell(row, 2).Style.Font.SetBold(true).Font.SetFontColor(Navy);
            ws.Cell(row, 3).Style.Alignment.SetIndent(1);
            ws.Cell(row, 4).Style.Font.SetItalic(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(row).Height = 18;
            row++;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Preliminaries — NRM2 Part 1 general preliminaries
        //  Substantial static text based on RICS / JCT / NEC boilerplate
        //  commonly used by senior QSs. Project-specific tokens substituted.
        // ══════════════════════════════════════════════════════════════════

        private void BuildPreliminariesSheet(IXLWorksheet ws, ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.Margins.Top = 0.8; ws.PageSetup.Margins.Bottom = 0.8;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;   // Clause ref
            ws.Column(3).Width = 80;   // Body
            SheetBanner(ws, "GENERAL PRELIMINARIES", m);

            int row = 5;
            IntroParagraph(ws, ref row,
                "These Preliminaries are prepared in accordance with the RICS New Rules of Measurement (NRM2), "
                + "Part 1 — General Preliminaries. They describe the Contract particulars, the Works, the Site, "
                + "and the obligations of the Contractor insofar as those obligations are not specifically measured "
                + "in the Bills of Quantities. The Contractor is deemed to have taken them into account when pricing "
                + "the Measured Works and no separate allowance is permitted elsewhere in this Bill.");

            var sections = new List<(string Code, string Title, string[] Clauses)>
            {
                ("1.1", "Contract particulars", new[]
                {
                    $"The Employer is {m.Employer}.",
                    $"The Project is \"{m.ProjectName}\" located at {m.ProjectAddress}.",
                    "The Form of Contract shall be JCT Standard Building Contract with Quantities (or equivalent East-African FIDIC-aligned form), "
                    + "executed as a deed, with the Contract Particulars as issued in the Invitation to Tender.",
                    $"The Quantity Surveyor acting on behalf of the Employer is {m.QsFirm}.",
                    $"The Principal Designer is {m.Architect}. The Services Engineer is {m.Engineer}.",
                    $"The Works Stage is {m.Stage}. Measurement has been undertaken in accordance with {m.MeasurementStandard}.",
                }),
                ("1.2", "Description of the Works", new[]
                {
                    "The Works comprise the design completion, procurement, construction, commissioning and handover of the Project "
                    + "in accordance with the Employer's Requirements, the Contractor's Proposals, the Drawings, the Specification "
                    + "and this Bill of Quantities.",
                    "The Contractor shall include in the Measured Works everything necessary to complete the Works, whether or not "
                    + "specifically described, so that the completed Works are fit for their intended purpose and comply with the "
                    + "performance requirements set out in the Specification.",
                }),
                ("1.3", "The Site", new[]
                {
                    "The Contractor is deemed to have inspected the Site and its surroundings before submitting its tender and to have "
                    + "satisfied itself as to the nature of the ground, access, the location of existing services, adjoining properties, "
                    + "and all matters and things necessary for the execution of the Works.",
                    "Setting-out reference points will be provided by the Engineer at the start of the Works. The Contractor shall verify "
                    + "all dimensions and levels on Site before commencing any work and shall promptly notify the Engineer in writing of any "
                    + "discrepancy discovered.",
                }),
                ("1.4", "Site possession, phasing and programme", new[]
                {
                    "Possession of the Site shall be given to the Contractor on the Date for Possession stated in the Contract Particulars.",
                    "The Contract Period and any Sectional Completion dates are as stated in the Contract Particulars. The Contractor shall "
                    + "submit a detailed Construction Programme within 14 days of possession showing all trades, lead times, milestones and "
                    + "float, and shall update the Programme monthly.",
                    "Working hours shall be 07:00 to 18:00 Monday to Saturday unless otherwise agreed in writing by the Employer. Any out-of-hours "
                    + "working necessary to meet the Programme shall be at the Contractor's cost.",
                }),
                ("1.5", "Site staff, offices and facilities", new[]
                {
                    "The Contractor shall provide and maintain for the duration of the Works: a site office for the Contract Administrator with "
                    + "desk, chair, filing cabinet, electronic communications, lighting and heating; welfare facilities for all operatives in "
                    + "accordance with Schedule 2 of the Construction (Design and Management) Regulations or the applicable national equivalent; "
                    + "drying and messing rooms; drinking water; first-aid room; and secure storage for materials.",
                    "The Contractor shall appoint a full-time Project Manager, a full-time Site Agent, a Health & Safety Officer, a Quality Manager, "
                    + "and such trade supervisors as are necessary to discharge its obligations under the Contract.",
                }),
                ("1.6", "Insurance, bonds and warranties", new[]
                {
                    "The Contractor shall maintain insurances in the amounts stated in the Contract Particulars, including Contractor's All Risks, "
                    + "Public Liability (minimum UGX 5,000,000,000), Employer's Liability, Professional Indemnity where design is included, and "
                    + "Works insurance in joint names of Employer and Contractor.",
                    "A Performance Bond equivalent to 10% of the Contract Sum and an Advance Payment Guarantee (if advance payment is requested) "
                    + "shall be provided within 14 days of the Letter of Acceptance.",
                    "Collateral Warranties shall be provided by the Contractor and by each Sub-contractor of Design Responsibility, in the form "
                    + "set out in the Preliminaries.",
                }),
                ("1.7", "Temporary works, hoarding and protection", new[]
                {
                    "The Contractor shall design, provide, maintain and remove on completion all temporary works, scaffolding, hoarding, protection "
                    + "sheeting, lighting to site and to the Public Highway, warning signs, and all necessary dust and noise mitigation.",
                    "Hoarding to the site perimeter shall be 2.4 m high solid timber with a single 4 m wide lockable vehicle gate and a separate "
                    + "pedestrian gate, finished on the external face with the Employer's project branding.",
                }),
                ("1.8", "Services — water, electricity, drainage, telecoms", new[]
                {
                    "The Contractor shall arrange and pay for all temporary services required for the Works, including water, electricity, drainage, "
                    + "compressed air and telecoms, and shall pay all standing charges and consumption charges until Practical Completion.",
                    "Permanent supplies shall be installed in accordance with the Specification and commissioned and handed over complete with "
                    + "all meters, connections and supply agreements transferred to the Employer.",
                }),
                ("1.9", "Testing, commissioning and handover", new[]
                {
                    "The Contractor shall carry out all testing and commissioning specified in the Specification or required by statute, including "
                    + "pressure testing of water and gas services, electrical test certificates (Part P / BS 7671), air-tightness testing, and "
                    + "BREEAM / EDGE / equivalent assessor witness tests where applicable.",
                    "O&M manuals (3 hard-copy sets + 1 searchable PDF), as-built drawings, record drawings, manufacturers' warranties, training of "
                    + "the Employer's facilities team, and a fully populated COBie spreadsheet to NRM1 / NRM3 asset classification shall be issued "
                    + "at Practical Completion. The Employer shall not be required to take possession until all such documentation is complete.",
                }),
                ("1.10", "Defects liability period, retention and final account", new[]
                {
                    "The Defects Liability Period shall be 12 months from the Date of Practical Completion. During this period the Contractor shall "
                    + "remedy at its own cost all defects, shrinkages and other faults due to materials or workmanship not in accordance with the Contract.",
                    "Retention shall be 5% of each Interim Certificate until Practical Completion, reducing to 2.5% thereafter and released in full "
                    + "at the end of the Defects Liability Period against the issue of the Certificate of Making Good Defects.",
                    "The Final Account shall be agreed within 3 months of the end of the Defects Liability Period.",
                }),
                ("1.11", "Method of measurement", new[]
                {
                    $"Unless otherwise stated, all items have been measured in accordance with {m.MeasurementStandard}. Where the NRM2 rules are "
                    + "silent or ambiguous, the prevailing custom of the RICS has been adopted.",
                    "Quantities are net as fixed in place with no deduction for voids less than 0.50 m² nor for chases, holes and mortices less "
                    + "than 0.05 m³. Waste, cutting, laps, fixings and jointings are deemed included in the rates unless separately measured.",
                }),
                ("1.12", "Provisional sums and contingencies", new[]
                {
                    "Provisional Sums (Defined and Undefined in the sense of NRM2) are included in the Bills for work that cannot be measured at "
                    + "the time of tender. The Contractor shall expend these sums only on the written instruction of the Contract Administrator "
                    + "and shall include pricing for main contractor's profit, attendance and on-costs against each Provisional Sum.",
                    $"A contractual Contingency of {m.ContingencyPct:F1}% has been added to the Sub-total in the Grand Summary. Contingency shall be "
                    + "expended only on the written instruction of the Contract Administrator and shall be reconciled at Final Account.",
                }),
            };

            foreach (var (code, title, clauses) in sections)
            {
                // Section title
                ws.Cell(row, 2).Value = code;
                ws.Cell(row, 3).Value = title.ToUpperInvariant();
                ws.Cell(row, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(Orange);
                ws.Cell(row, 3).Style.Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(Navy);
                ws.Range(row, 2, row, 3).Style.Border.SetBottomBorder(XLBorderStyleValues.Thin)
                    .Border.SetBottomBorderColor(Navy);
                ws.Row(row).Height = 20;
                row++;

                foreach (var clause in clauses)
                {
                    ws.Cell(row, 3).Value = clause;
                    ws.Cell(row, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                    // Make Excel auto-size text rows — approximate
                    ws.Row(row).Height = Math.Max(18, Math.Min(120, 2 + clause.Length / 6));
                    row++;
                }
                row++;
            }
        }

        private void IntroParagraph(IXLWorksheet ws, ref int row, string text)
        {
            ws.Cell(row, 3).Value = text;
            ws.Cell(row, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(90, 90, 90))
                .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            ws.Row(row).Height = 68;
            row += 2;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Preambles — NRM2 Part 2 per-discipline materials & workmanship
        // ══════════════════════════════════════════════════════════════════

        private void BuildPreamblesSheet(IXLWorksheet ws, BOQDocument boq)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 80;

            // Banner (local — we don't have m here, make a simple one)
            ws.Cell(1, 2).Value = "PREAMBLES TO TRADES";
            ws.Range(1, 2, 1, 3).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(16).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(1).Height = 32;

            ws.Cell(2, 3).Value = "Materials and workmanship standards applicable to each trade section. "
                + "The standards and codes referenced below are minimum requirements; "
                + "where the Specification or Drawings impose higher standards those shall prevail.";
            ws.Cell(2, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(90, 90, 90))
                .Alignment.SetWrapText(true);
            ws.Row(2).Height = 40;

            int row = 5;
            var disciplineGroups = boq.Sections
                .GroupBy(s => s.Discipline ?? "GEN")
                .OrderBy(g => DisciplineRank(g.Key));

            foreach (var dg in disciplineGroups)
            {
                string discName = DisciplineName(dg.Key);
                ws.Cell(row, 2).Value = dg.Key;
                ws.Cell(row, 3).Value = discName.ToUpperInvariant();
                ws.Cell(row, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(12).Font.SetBold(true).Font.SetFontColor(Orange);
                ws.Cell(row, 3).Style.Font.SetFontName(HeadFont).Font.SetFontSize(12).Font.SetBold(true).Font.SetFontColor(Navy);
                ws.Range(row, 2, row, 3).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium)
                    .Border.SetBottomBorderColor(Navy);
                ws.Row(row).Height = 22;
                row++;

                foreach (var clause in GetDisciplinePreambles(dg.Key))
                {
                    ws.Cell(row, 3).Value = clause;
                    ws.Cell(row, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                    ws.Row(row).Height = Math.Max(18, Math.Min(120, 2 + clause.Length / 6));
                    row++;
                }

                // Reference codes
                ws.Cell(row, 2).Value = "Codes";
                ws.Cell(row, 3).Value = GetDisciplineCodes(dg.Key);
                ws.Range(row, 2, row, 3).Style.Fill.SetBackgroundColor(GreyLight);
                ws.Cell(row, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(9).Font.SetBold(true)
                    .Font.SetFontColor(XLColor.FromArgb(110, 110, 110));
                ws.Cell(row, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(9).Font.SetItalic(true)
                    .Alignment.SetWrapText(true);
                ws.Row(row).Height = 30;
                row += 2;
            }
        }

        private string[] GetDisciplinePreambles(string disc)
        {
            switch (disc)
            {
                case "A":
                    return new[] {
                        "All materials shall be new, of the best of their respective kinds, and shall comply with the latest edition of the relevant British or European standards. Where no specific standard is cited, materials shall be equal in quality and performance to those commonly accepted in good building practice.",
                        "Workmanship shall be of the highest quality consistent with the intended use of the building. All concealed work shall be to the same standard as exposed work. The Contractor shall maintain sample panels of brickwork, rendering, plastering and key finishes for approval before the main work proceeds.",
                        "Setting-out, tolerances and levelness of finished surfaces shall comply with BS 8000: Workmanship on building sites. Squareness, plumb and plane tolerances for walls, floors and ceilings shall be ±3 mm in 3 m unless otherwise specified.",
                    };
                case "S":
                    return new[] {
                        "Structural concrete shall comply with BS EN 206 / BS 8500. Design strength, exposure class, minimum cement content, maximum water/cement ratio and slump shall be as stated on the Structural Drawings. Concrete shall be supplied by a BSI Kitemark-approved ready-mix plant.",
                        "Reinforcement shall be grade B500B or B500C in accordance with BS 4449. Bar bending schedules shall be prepared by the Contractor to BS 8666. Minimum concrete cover shall be as shown on the Drawings and not less than the exposure-class minimum of BS EN 1992-1-1.",
                        "Structural steelwork shall be grade S355 JR / J0 in accordance with BS EN 10025 and fabricated and erected in accordance with BS EN 1090-2. All welding shall be carried out by welders certified to BS EN ISO 9606. Corrosion protection shall be as specified.",
                    };
                case "M":
                    return new[] {
                        "All mechanical installations shall be designed, installed, tested and commissioned in accordance with CIBSE Guides A, B, C and H, the relevant sections of the Building Services Research and Information Association (BSRIA) application guides and the Health Technical Memoranda where applicable.",
                        "Duct velocities shall not exceed the CIBSE Guide B recommended limits for the system type. Acoustic attenuation shall be provided to achieve the NR levels stated in the Specification. All supports, vibration isolation and acoustic insulation shall be included.",
                        "Testing and commissioning shall comply with BSRIA BG 49 (Commissioning Air Systems), BG 2 (Commissioning Water Systems) and the CIBSE Commissioning Code M. Witness testing by the Engineer shall be included.",
                    };
                case "E":
                    return new[] {
                        "Electrical installations shall comply with BS 7671 (IET Wiring Regulations, 18th Edition including Amendments) and the Electricity at Work Regulations. All completed installations shall be inspected, tested and certified by a competent person and the Electrical Installation Certificate and Minor Works Certificates issued to the Employer.",
                        "Distribution equipment — panels, distribution boards, submains — shall comply with BS EN 61439. Final circuits shall use LSZH (Low Smoke Zero Halogen) cable to BS 6724 / BS EN 50525 where installed in areas of public access or on escape routes.",
                        "Earthing and bonding shall comply with BS 7430. Surge protection devices (SPDs) in accordance with BS EN 62305 shall be provided on the main incomer and at distribution-board level where specified.",
                    };
                case "P":
                    return new[] {
                        "Plumbing installations shall comply with BS EN 806 (water), BS EN 12056 (drainage), BS 8558 (potable water in buildings) and the Water Supply (Water Fittings) Regulations.",
                        "Hot and cold water services shall be tested to 1.5× working pressure for a minimum of 1 hour with no measurable drop. All potable water pipework shall be disinfected in accordance with BS EN 806-4 and a bacteriological clearance certificate issued.",
                        "Sanitaryware shall comply with BS EN 997 (WCs), BS EN 14688 (basins) and similar applicable product standards. Fixings, supports and access for maintenance shall be in accordance with BS 8313.",
                    };
                case "FP":
                    return new[] {
                        "Fire protection installations shall comply with Approved Document B, BS 9999, BS 9991, and the relevant sections of LPS 1014 where sprinkler systems are specified.",
                        "Sprinkler systems shall be designed, installed and commissioned in accordance with BS EN 12845. Wet and dry riser installations shall comply with BS 9990. All passive fire protection shall comply with BS 476 / BS EN 1366 test standards.",
                        "Fire alarm and voice alarm systems shall comply with BS 5839-1 and BS 5839-8 respectively, designed to the category stated in the Specification. Installation, commissioning and certification shall be by a BAFE SP203-1 registered company.",
                    };
                default:
                    return new[] {
                        "All materials and workmanship shall be in accordance with the relevant British Standards, European Standards and the Building Regulations in force at the date of this Contract. Where no specific standard is quoted, materials shall be equal to those commonly accepted in good building practice.",
                    };
            }
        }

        private string GetDisciplineCodes(string disc)
        {
            switch (disc)
            {
                case "A":  return "BS 8000, BS 5628, BS EN 13914, BS 8204, BS EN 13300, BS 6229, BS 5250, Approved Documents A, C, E, K, L, M.";
                case "S":  return "BS EN 1992 / 1993 / 1997, BS 8500, BS 4449, BS 8666, BS EN 10025, BS EN 1090-2, BS EN ISO 9606, Approved Document A.";
                case "M":  return "CIBSE Guides A / B / C / H, BSRIA BG 49 / BG 2, CIBSE Commissioning Code M, HTM 03-01, BS EN 12237.";
                case "E":  return "BS 7671 (18th Ed.), BS EN 61439, BS 6724, BS 7430, BS EN 62305, Approved Document P, IEC 60364.";
                case "P":  return "BS EN 806, BS EN 12056, BS 8558, BS EN 997, BS 8313, Water Supply (Water Fittings) Regulations.";
                case "FP": return "BS 9999, BS 9991, BS EN 12845, BS 9990, BS 5839-1, BS 5839-8, BS 476 / BS EN 1366, LPS 1014, Approved Document B.";
                default:   return "Relevant British Standards, European Standards and the Building Regulations in force.";
            }
        }

        private int DisciplineRank(string d)
        {
            switch (d)
            {
                case "A":  return 1;
                case "S":  return 2;
                case "M":  return 3;
                case "E":  return 4;
                case "P":  return 5;
                case "FP": return 6;
                case "PS": return 8;
                default:   return 7;
            }
        }

        private string DisciplineName(string d)
        {
            switch (d)
            {
                case "A":  return "Architectural";
                case "S":  return "Structural";
                case "M":  return "Mechanical";
                case "E":  return "Electrical";
                case "P":  return "Plumbing & Drainage";
                case "FP": return "Fire Protection";
                case "PS": return "Provisional Sums";
                default:   return "General";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Bill sheets — one per BOQ section. Standard QS layout:
        //  Ref | Description | Unit | Quantity | Rate | Amount
        //  with "Carried to Collection" footer.
        // ══════════════════════════════════════════════════════════════════

        private double BuildBillSheet(IXLWorksheet ws, BOQSection sec, ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.Margins.Top = 0.6; ws.PageSetup.Margins.Bottom = 0.6;
            ws.ShowGridLines = false;

            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;  // Ref
            ws.Column(3).Width = 78;  // Description
            ws.Column(4).Width = 7;   // Unit
            ws.Column(5).Width = 12;  // Qty
            ws.Column(6).Width = 14;  // Rate
            ws.Column(7).Width = 16;  // Amount

            // Sheet header band
            ws.Cell(1, 2).Value = $"SECTION {sec.NRM2Section} — {sec.Name.ToUpperInvariant()}";
            ws.Range(1, 2, 1, 7).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(14).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(1).Height = 28;

            ws.Cell(2, 2).Value = $"{m.ProjectName}  ·  Rev {m.Revision}  ·  {m.RevisionDate}  ·  {m.TenderStatus}";
            ws.Range(2, 2, 2, 7).Merge().Style
                .Font.SetFontName(BodyFont).Font.SetFontSize(9).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(110, 110, 110))
                .Alignment.SetIndent(1)
                .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(Orange);
            ws.Row(2).Height = 18;

            // Column headers
            int r = 4;
            string[] headers = { "Ref", "Description", "Unit", "Quantity", "Rate", "Amount" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(r, 2 + i);
                c.Value = headers[i];
                c.Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Font.SetFontColor(White)
                    .Fill.SetBackgroundColor(Navy)
                    .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Navy)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                    .Alignment.SetHorizontal(i == 0 ? XLAlignmentHorizontalValues.Center
                        : i == 1 ? XLAlignmentHorizontalValues.Left
                        : i == 2 ? XLAlignmentHorizontalValues.Center
                        : XLAlignmentHorizontalValues.Right);
            }
            ws.Row(r).Height = 22;
            r++;

            // Header rows repeat on print
            ws.PageSetup.SetRowsToRepeatAtTop(1, r - 1);

            double total = 0;
            var sortedItems = sec.Items
                .OrderBy(i => i.Source == BOQRowSource.ProvisionalSum ? 2 : i.Source == BOQRowSource.Manual ? 1 : 0)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.Category)
                .ThenBy(i => i.ItemName)
                .ToList();

            int itemIdx = 0;
            foreach (var item in sortedItems)
            {
                itemIdx++;
                string itemRef = string.IsNullOrEmpty(item.BOQLineRef) ? $"{sec.NRM2Section}.{itemIdx:000}" : item.BOQLineRef;

                ws.Cell(r, 2).Value = itemRef;
                ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(9).Font.SetBold(true)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);

                // Description — item name line + NRM2 paragraph wrapped below
                string para = string.IsNullOrEmpty(item.ResolvedNRM2Paragraph)
                    ? $"Supply, fix and install {(item.Category ?? item.ItemName ?? "the Works").ToLowerInvariant()}."
                    : item.ResolvedNRM2Paragraph;
                ws.Cell(r, 3).Value = para;
                ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);

                ws.Cell(r, 4).Value = PrettifyUnit(item.Unit);
                ws.Cell(r, 4).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);

                ws.Cell(r, 5).Value = item.Quantity;
                ws.Cell(r, 5).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .NumberFormat.SetFormat("#,##0.00")
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);

                if (item.RateUGX > 0)
                {
                    ws.Cell(r, 6).Value = item.RateUGX;
                    ws.Cell(r, 6).Style.NumberFormat.SetFormat("#,##0.00");
                }
                else
                {
                    ws.Cell(r, 6).Value = "-";
                    ws.Cell(r, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }
                ws.Cell(r, 6).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                if (item.RateUGX > 0)
                    ws.Cell(r, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                double amount = item.TotalUGX;
                total += amount;
                if (amount > 0)
                {
                    ws.Cell(r, 7).Value = amount;
                    ws.Cell(r, 7).Style.NumberFormat.SetFormat("#,##0.00");
                }
                else
                {
                    ws.Cell(r, 7).Value = "-";
                    ws.Cell(r, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }
                ws.Cell(r, 7).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                if (amount > 0)
                    ws.Cell(r, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                // Row background for PS / Manual
                if (item.Source == BOQRowSource.ProvisionalSum)
                {
                    ws.Range(r, 2, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromArgb(237, 231, 246));
                    ws.Cell(r, 3).Value = "PROVISIONAL SUM: " + para;
                }
                else if (item.Source == BOQRowSource.Manual)
                {
                    ws.Range(r, 2, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromArgb(255, 251, 230));
                }

                // Light underline
                for (int i = 2; i <= 7; i++)
                    ws.Cell(r, i).Style.Border.SetBottomBorder(XLBorderStyleValues.Hair)
                        .Border.SetBottomBorderColor(GreyMid);
                // Row height scales with description length
                ws.Row(r).Height = Math.Max(22, Math.Min(90, 4 + para.Length / 7));
                r++;
            }

            // Carried-to-Collection footer
            r++;
            ws.Cell(r, 3).Value = $"CARRIED TO COLLECTION — SECTION {sec.NRM2Section}";
            ws.Cell(r, 7).Value = total;
            ws.Range(r, 2, r, 7).Style
                .Fill.SetBackgroundColor(GreyLight)
                .Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                .Font.SetFontColor(Navy)
                .Border.SetTopBorder(XLBorderStyleValues.Medium).Border.SetTopBorderColor(Navy)
                .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Navy);
            ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(r, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .NumberFormat.SetFormat("#,##0.00");
            ws.Row(r).Height = 22;

            ws.SheetView.FreezeRows(4);
            return total;
        }

        private string PrettifyUnit(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "each";
            string u = raw.Trim();
            switch (u.ToLowerInvariant())
            {
                case "m2": return "m²";
                case "m3": return "m³";
                case "sqm": return "m²";
                case "cum": return "m³";
                case "ltm": return "m";
                case "lm":  return "m";
                default: return u;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Collections — each section total rolled to a single sheet grouped
        //  by discipline. Totals carry to the Grand Summary.
        // ══════════════════════════════════════════════════════════════════

        private void BuildCollectionsSheet(IXLWorksheet ws,
            List<(BOQSection Sec, string SheetName, double TotalUGX)> rows,
            ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 58;
            ws.Column(4).Width = 20;

            SheetBanner(ws, "COLLECTIONS", m);

            int r = 5;
            var disciplineGroups = rows
                .GroupBy(x => x.Sec.Discipline ?? "GEN")
                .OrderBy(g => DisciplineRank(g.Key));

            foreach (var grp in disciplineGroups)
            {
                // Discipline band
                ws.Cell(r, 2).Value = grp.Key;
                ws.Cell(r, 3).Value = DisciplineName(grp.Key).ToUpperInvariant();
                ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(Orange);
                ws.Cell(r, 3).Style.Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(Navy);
                ws.Range(r, 2, r, 4).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium)
                    .Border.SetBottomBorderColor(Navy)
                    .Fill.SetBackgroundColor(GreyLight);
                ws.Row(r).Height = 20;
                r++;

                double discTotal = 0;
                foreach (var row in grp.OrderBy(x => x.Sec.NRM2Section))
                {
                    ws.Cell(r, 2).Value = row.Sec.NRM2Section;
                    ws.Cell(r, 3).Value = row.Sec.Name;
                    ws.Cell(r, 4).Value = row.TotalUGX;
                    ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .Alignment.SetIndent(1);
                    ws.Cell(r, 4).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .NumberFormat.SetFormat("#,##0.00")
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    for (int i = 2; i <= 4; i++)
                        ws.Cell(r, i).Style.Border.SetBottomBorder(XLBorderStyleValues.Hair).Border.SetBottomBorderColor(GreyMid);
                    discTotal += row.TotalUGX;
                    r++;
                }

                // Discipline subtotal
                ws.Cell(r, 3).Value = $"TOTAL — {DisciplineName(grp.Key).ToUpperInvariant()}";
                ws.Cell(r, 4).Value = discTotal;
                ws.Cell(r, 3).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true).Font.SetFontColor(Navy)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                ws.Cell(r, 4).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true).Font.SetFontColor(Navy)
                    .NumberFormat.SetFormat("#,##0.00")
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                ws.Range(r, 2, r, 4).Style
                    .Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(Navy)
                    .Border.SetBottomBorder(XLBorderStyleValues.Double).Border.SetBottomBorderColor(Navy);
                r += 2;
            }

            // Grand sub-total of measured works (before prelims / contingency)
            double grand = rows.Sum(x => x.TotalUGX);
            ws.Cell(r, 3).Value = "TOTAL MEASURED WORKS — CARRIED TO GRAND SUMMARY";
            ws.Cell(r, 4).Value = grand;
            ws.Range(r, 2, r, 4).Style
                .Fill.SetBackgroundColor(Navy)
                .Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(White)
                .Border.SetTopBorder(XLBorderStyleValues.Double).Border.SetTopBorderColor(Navy)
                .Border.SetBottomBorder(XLBorderStyleValues.Double).Border.SetBottomBorderColor(Navy);
            ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(r, 4).Style.NumberFormat.SetFormat("#,##0.00")
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(r).Height = 24;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Grand Summary — the single most scrutinised page of a BOQ.
        //  Measured works + prelims + PS + contingency + OH&P + VAT = Sum.
        // ══════════════════════════════════════════════════════════════════

        private void BuildGrandSummarySheet(IXLWorksheet ws, BOQDocument boq,
            List<(BOQSection Sec, string SheetName, double TotalUGX)> rows,
            ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 58;
            ws.Column(4).Width = 22;

            SheetBanner(ws, "GRAND SUMMARY", m);

            double measured = rows.Sum(x => x.TotalUGX);
            double prelims = measured * (m.PrelimPct / 100.0);
            double contingency = measured * (m.ContingencyPct / 100.0);
            double overhead = measured * (m.OverheadPct / 100.0);
            double subTotal = measured + prelims + contingency + overhead;
            double vat = subTotal * (m.VatPct / 100.0);
            double contractSum = subTotal + vat;

            int r = 5;
            var lines = new List<(string Code, string Label, double? Amount, bool Heavy)>
            {
                ("A", "Total Measured Works (from Collections)", measured, false),
                ("B", $"General Preliminaries ({m.PrelimPct:F1}%)", prelims, false),
                ("C", $"Contingency ({m.ContingencyPct:F1}%)", contingency, false),
                ("D", $"Main Contractor's Overhead & Profit ({m.OverheadPct:F1}%)", overhead, false),
                (null, null, null, false),
                ("",  "SUB-TOTAL EXCLUSIVE OF TAX", subTotal, true),
                (null, null, null, false),
                ("E", $"Value-Added Tax ({m.VatPct:F1}%)", vat, false),
                (null, null, null, false),
                ("",  "CONTRACT SUM", contractSum, true),
            };

            foreach (var (code, label, amt, heavy) in lines)
            {
                if (code == null && label == null)
                {
                    r++;
                    continue;
                }

                ws.Cell(r, 2).Value = code;
                ws.Cell(r, 3).Value = label;
                if (amt.HasValue)
                {
                    ws.Cell(r, 4).Value = amt.Value;
                    ws.Cell(r, 4).Style.NumberFormat.SetFormat("#,##0.00");
                }

                if (heavy)
                {
                    ws.Range(r, 2, r, 4).Style
                        .Fill.SetBackgroundColor(label.Contains("CONTRACT") ? Navy : GreyLight)
                        .Font.SetFontName(HeadFont).Font.SetFontSize(label.Contains("CONTRACT") ? 14 : 11).Font.SetBold(true)
                        .Font.SetFontColor(label.Contains("CONTRACT") ? White : Navy)
                        .Border.SetTopBorder(XLBorderStyleValues.Double).Border.SetTopBorderColor(Navy)
                        .Border.SetBottomBorder(XLBorderStyleValues.Double).Border.SetBottomBorderColor(Navy);
                    ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Cell(r, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Row(r).Height = label.Contains("CONTRACT") ? 32 : 24;
                }
                else
                {
                    ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(11).Font.SetBold(true).Font.SetFontColor(Orange)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(11)
                        .Alignment.SetIndent(1);
                    ws.Cell(r, 4).Style.Font.SetFontName(BodyFont).Font.SetFontSize(11).Font.SetBold(true)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Range(r, 2, r, 4).Style
                        .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(GreyMid);
                    ws.Row(r).Height = 22;
                }
                r++;
            }

            // Currency note
            r += 2;
            ws.Cell(r, 3).Value = $"All figures are in {m.Currency}. Exchange rate applied: UGX {m.ExchangeRate:N0} / USD.";
            ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(9).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(110, 110, 110));

            // Signature block
            r += 3;
            ws.Cell(r, 2).Value = "Signed for and on behalf of the Contractor:";
            ws.Range(r, 2, r, 4).Merge().Style
                .Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true);
            r += 2;
            ws.Cell(r, 2).Value = "Name"; ws.Cell(r, 3).Value = "";
            ws.Range(r, 3, r, 4).Merge().Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            r += 2;
            ws.Cell(r, 2).Value = "Title"; ws.Range(r, 3, r, 4).Merge().Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            r += 2;
            ws.Cell(r, 2).Value = "Date"; ws.Range(r, 3, r, 4).Merge().Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            r += 2;
            ws.Cell(r, 2).Value = "Company Stamp";
            ws.Range(r, 3, r, 4).Merge().Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);

            for (int i = r - 8; i <= r; i += 2)
                if (i >= 5) ws.Cell(i, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(9).Font.SetBold(true)
                    .Font.SetFontColor(XLColor.FromArgb(110, 110, 110));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Annexure — Prime Cost Sums, Provisional Sums, Dayworks Schedule
        // ══════════════════════════════════════════════════════════════════

        private void BuildAnnexureSheet(IXLWorksheet ws, BOQDocument boq, ProjectMeta m)
        {
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 56;
            ws.Column(4).Width = 18;

            SheetBanner(ws, "ANNEXURE — PROVISIONAL SUMS & DAYWORKS", m);

            int r = 5;

            // Provisional Sums table
            ws.Cell(r, 2).Value = "PROVISIONAL SUMS";
            ws.Range(r, 2, r, 4).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(12).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetIndent(1);
            ws.Row(r).Height = 24;
            r += 2;

            ws.Cell(r, 2).Value = "Ref";
            ws.Cell(r, 3).Value = "Description";
            ws.Cell(r, 4).Value = "Amount (UGX)";
            for (int i = 2; i <= 4; i++)
                ws.Cell(r, i).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Font.SetFontColor(Navy).Fill.SetBackgroundColor(GreyLight)
                    .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Navy);
            ws.Cell(r, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(r).Height = 20;
            r++;

            var psItems = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            if (psItems.Count == 0)
            {
                ws.Cell(r, 3).Value = "(No Provisional Sums — to be confirmed at Final Account)";
                ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true)
                    .Font.SetFontColor(XLColor.FromArgb(110, 110, 110));
                r++;
            }
            else
            {
                int idx = 0;
                foreach (var ps in psItems.OrderBy(p => p.NRM2Section))
                {
                    idx++;
                    ws.Cell(r, 2).Value = $"PS.{idx:00}";
                    ws.Cell(r, 3).Value = string.IsNullOrEmpty(ps.ResolvedNRM2Paragraph)
                        ? ps.ItemName : ps.ResolvedNRM2Paragraph;
                    ws.Cell(r, 4).Value = ps.TotalUGX;
                    ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .Alignment.SetWrapText(true).Alignment.SetIndent(1);
                    ws.Cell(r, 4).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                        .NumberFormat.SetFormat("#,##0.00")
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    ws.Row(r).Height = 26;
                    r++;
                }
                // Total
                ws.Cell(r, 3).Value = "TOTAL PROVISIONAL SUMS";
                ws.Cell(r, 4).Value = psItems.Sum(p => p.TotalUGX);
                ws.Range(r, 2, r, 4).Style
                    .Fill.SetBackgroundColor(GreyLight)
                    .Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true).Font.SetFontColor(Navy)
                    .Border.SetTopBorder(XLBorderStyleValues.Medium).Border.SetTopBorderColor(Navy)
                    .Border.SetBottomBorder(XLBorderStyleValues.Double).Border.SetBottomBorderColor(Navy);
                ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                ws.Cell(r, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                    .NumberFormat.SetFormat("#,##0.00");
                ws.Row(r).Height = 22;
                r++;
            }

            // Dayworks Schedule (rates framework, quantities priced at final account)
            r += 2;
            ws.Cell(r, 2).Value = "DAYWORKS SCHEDULE";
            ws.Range(r, 2, r, 4).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(12).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetIndent(1);
            ws.Row(r).Height = 24;
            r += 2;

            ws.Cell(r, 3).Value = "The Contractor shall provide all-inclusive hourly rates for dayworks when instructed by the "
                + "Contract Administrator under Clause 5.7 of the Contract. Rates shall include wages, allowances, "
                + "supervision, overhead, profit and the use of small plant and tools. Materials at net cost plus the "
                + "Contractor's overhead and profit percentage stated below.";
            ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(90, 90, 90))
                .Alignment.SetWrapText(true);
            ws.Row(r).Height = 48;
            r += 2;

            var dwRows = new[]
            {
                ("DW.01", "General labourer (unskilled)"),
                ("DW.02", "Semi-skilled labourer"),
                ("DW.03", "Skilled tradesman — carpentry / masonry"),
                ("DW.04", "Skilled tradesman — electrical / plumbing"),
                ("DW.05", "Foreman / chargehand"),
                ("DW.06", "Site engineer / setter-out"),
                (null,   null),
                ("DW.10", "Materials — percentage addition for OH&P"),
                ("DW.11", "Plant at cost — percentage addition for OH&P"),
                ("DW.12", "Sub-contractors at cost — percentage addition for OH&P"),
            };

            ws.Cell(r, 2).Value = "Ref";
            ws.Cell(r, 3).Value = "Resource";
            ws.Cell(r, 4).Value = "Rate (UGX / hr or %)";
            for (int i = 2; i <= 4; i++)
                ws.Cell(r, i).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Font.SetFontColor(Navy).Fill.SetBackgroundColor(GreyLight)
                    .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Navy);
            ws.Cell(r, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(r).Height = 20;
            r++;

            foreach (var (refCode, res) in dwRows)
            {
                if (refCode == null) { r++; continue; }
                ws.Cell(r, 2).Value = refCode;
                ws.Cell(r, 3).Value = res;
                // Rate blank — for the bidder to fill in
                ws.Cell(r, 2).Style.Font.SetFontName(HeadFont).Font.SetFontSize(10).Font.SetBold(true)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 3).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Alignment.SetIndent(1);
                ws.Cell(r, 4).Style.Font.SetFontName(BodyFont).Font.SetFontSize(10)
                    .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(GreyMid)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                ws.Row(r).Height = 22;
                r++;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Shared helpers
        // ══════════════════════════════════════════════════════════════════

        private void SheetBanner(IXLWorksheet ws, string title, ProjectMeta m)
        {
            ws.Cell(1, 2).Value = title;
            ws.Range(1, 2, 1, 7).Merge().Style
                .Font.SetFontName(HeadFont).Font.SetFontSize(16).Font.SetBold(true)
                .Font.SetFontColor(White).Fill.SetBackgroundColor(Navy)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetIndent(1);
            ws.Row(1).Height = 32;

            ws.Cell(2, 2).Value = $"{m.ProjectName}  ·  {m.ProjectNumber}  ·  Rev {m.Revision}  ·  {m.RevisionDate}";
            ws.Range(2, 2, 2, 7).Merge().Style
                .Font.SetFontName(BodyFont).Font.SetFontSize(9).Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromArgb(110, 110, 110))
                .Alignment.SetIndent(1)
                .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(Orange);
            ws.Row(2).Height = 18;
        }

        private static string SafeSheetName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Sheet";
            // Excel sheet names: max 31 chars, no \ / ? * [ ] :
            string clean = new string(raw.Select(c => "\\/?*[]:".IndexOf(c) >= 0 ? ' ' : c).ToArray()).Trim();
            if (clean.Length > 31) clean = clean.Substring(0, 31).Trim();
            return string.IsNullOrEmpty(clean) ? "Sheet" : clean;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Paragraph resolution guardrail — copied from BOQExportCommand so
        //  both exports share behaviour. Replaces empty or token-containing
        //  paragraphs with a deterministic fallback sentence.
        // ══════════════════════════════════════════════════════════════════

        private static readonly System.Text.RegularExpressions.Regex _tokenRx
            = new System.Text.RegularExpressions.Regex(@"\[[A-Za-z0-9_]+\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private void EnsureAllParagraphsResolved(BOQDocument boq, Document doc)
        {
            if (boq == null) return;
            foreach (var sec in boq.Sections)
            {
                foreach (var item in sec.Items)
                {
                    bool missing = string.IsNullOrEmpty(item.ResolvedNRM2Paragraph);
                    bool hasTokens = !missing && _tokenRx.IsMatch(item.ResolvedNRM2Paragraph);
                    if (!missing && !hasTokens) continue;
                    if (item.RevitElementId > 0)
                    {
                        try
                        {
                            var el = doc.GetElement(new ElementId(item.RevitElementId));
                            if (el != null)
                            {
                                var all = StingTools.Temp.BOQTemplateLibrary.LoadAll(doc, StingToolsApp.DataPath);
                                var tpl = BOQTemplateLibraryExtensions.SelectBestTemplate(all, item.Category, el);
                                if (tpl != null)
                                {
                                    string resolved = BOQTemplateLibraryExtensions.ResolveForElement(tpl, el, doc);
                                    if (!string.IsNullOrEmpty(resolved) && !_tokenRx.IsMatch(resolved))
                                    {
                                        item.ResolvedNRM2Paragraph = resolved;
                                        continue;
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Resolve paragraph for {item.RevitElementId}: {ex.Message}"); }
                    }
                    item.ResolvedNRM2Paragraph = BuildFallbackParagraph(item);
                }
            }
        }

        private static string BuildFallbackParagraph(BOQLineItem item)
        {
            string cat = item.Category?.ToLowerInvariant() ?? "item";
            string fam = !string.IsNullOrEmpty(item.FamilyName) && item.FamilyName != "—"
                ? $" ({item.FamilyName})" : "";
            string loc = !string.IsNullOrEmpty(item.Location) ? $" at {item.Location}" : "";
            string lvl = !string.IsNullOrEmpty(item.Level) ? $" on {item.Level}" : "";
            string disc;
            switch (item.Discipline)
            {
                case "S":  disc = "structural";      break;
                case "M":  disc = "mechanical";      break;
                case "E":  disc = "electrical";      break;
                case "P":  disc = "plumbing";        break;
                case "FP": disc = "fire protection"; break;
                default:   disc = "architectural";   break;
            }
            return $"Supply, deliver and install {disc} {cat}{fam}{loc}{lvl}; "
                 + "including all associated fixings, connections, and accessories; "
                 + "completed and tested to the relevant British Standard or equivalent.";
        }
    }
}
