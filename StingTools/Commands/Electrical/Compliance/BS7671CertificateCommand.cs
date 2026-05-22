using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// Generates a BS 7671:2018+A2:2022 Appendix 6 Initial Verification
    /// Certificate template, pre-filled from Project Information + the
    /// most recent BS 7671 audit results. Empty cells stay blank for the
    /// inspecting electrician to write in measured Zs / insulation
    /// resistance / RCD trip times during commissioning.
    ///
    /// Layout: cover page (Particulars of installation, Designer / Installer
    /// / Inspector signatures), Schedule of Inspections checklist, Schedule
    /// of Circuit Details (one row per final circuit), Schedule of Test
    /// Results (Zs / insulation R / polarity / RCD test). Output is .xlsx
    /// because every UK electrician uses Excel-based certificate forms in
    /// practice and they print to A4 unchanged.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BS7671CertificateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var auditRows = StingElectricalCommandHandler.LastBs7671Results ?? new List<CircuitAuditResult>();
            string earthing = StingElectricalCommandHandler.CurrentEarthingSystem ?? "TN-C-S";

            var pi = doc.ProjectInformation;
            string projectName = pi?.Name ?? "";
            string projectNumber = pi?.Number ?? "";
            string projectAddress = pi?.Address ?? "";
            string client = pi?.ClientName ?? "";

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_BS7671_Certificate_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

            using var wb = new XLWorkbook();
            WriteCoverSheet(wb.Worksheets.Add("1 Cover"), projectName, projectNumber, projectAddress, client, earthing);
            WriteInspectionSheet(wb.Worksheets.Add("2 Inspections"));
            WriteCircuitDetailsSheet(wb.Worksheets.Add("3 Circuit Details"), auditRows);
            WriteTestResultsSheet(wb.Worksheets.Add("4 Test Results"), auditRows);

            try { wb.SaveAs(outPath); }
            catch (Exception ex) { StingLog.Error($"Cert save: {ex.Message}", ex); msg = ex.Message; return Result.Failed; }

            TaskDialog.Show("STING BS 7671 Certificate",
                $"Generated Initial Verification Certificate template:\n{outPath}\n\n" +
                $"Filled {auditRows.Count} circuit row(s) from the latest audit. " +
                "Designer / installer / inspector signature blocks left blank for hand-completion.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static void WriteCoverSheet(IXLWorksheet ws, string projectName, string projectNumber,
            string projectAddress, string client, string earthing)
        {
            ws.Cell(1, 1).Value = "BS 7671:2018 + A2:2022";
            ws.Cell(2, 1).Value = "ELECTRICAL INSTALLATION CERTIFICATE — Initial Verification";
            ws.Range(1, 1, 1, 4).Merge().Style.Font.Bold = true;
            ws.Range(2, 1, 2, 4).Merge().Style.Font.Bold = true;
            ws.Range(2, 1, 2, 4).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            ws.Range(2, 1, 2, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 4;
            void Field(string label, string value)
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = value ?? "";
                ws.Range(row, 2, row, 4).Merge();
                ws.Range(row, 1, row, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row++;
            }
            ws.Cell(row, 1).Value = "PARTICULARS OF THE INSTALLATION";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;
            Field("Client",            client);
            Field("Project name",      projectName);
            Field("Project number",    projectNumber);
            Field("Installation address", projectAddress);
            Field("Earthing arrangement", earthing);
            Field("Number of phases",  "");
            Field("Nominal voltage Uo (V)", "230");
            Field("Maximum demand (kVA)", "");
            Field("Prospective fault current Ipf (kA)", "");
            Field("Loop impedance Ze at origin (Ω)", "");
            row++;
            ws.Cell(row, 1).Value = "DESIGNER / INSTALLER / INSPECTOR";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;
            string[] roles = { "Designer", "Installer (Constructor)", "Inspector / Tester" };
            foreach (var r in roles)
            {
                ws.Cell(row, 1).Value = r; ws.Cell(row, 1).Style.Font.Bold = true; row++;
                Field("  Name", "");
                Field("  Company", "");
                Field("  Position / qualification", "");
                Field("  Signature", "");
                Field("  Date", "");
                row++;
            }

            ws.Cell(row, 1).Value = "DECLARATION";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            row++;
            ws.Cell(row, 1).Value =
                "I/We being the persons responsible for the design, construction, inspection and testing " +
                "of the electrical installation (as indicated by my/our signatures below), particulars of " +
                "which are described, having exercised reasonable skill and care, hereby CERTIFY that the " +
                "said work for which I/we have been responsible is to the best of my/our knowledge and " +
                "belief in accordance with BS 7671:2018+A2:2022, except for the departures, if any, stated.";
            ws.Range(row, 1, row + 4, 4).Merge().Style.Alignment.WrapText = true;

            ws.Columns().AdjustToContents();
            ws.Column(2).Width = Math.Max(ws.Column(2).Width, 35);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        }

        private static void WriteInspectionSheet(IXLWorksheet ws)
        {
            ws.Cell(1, 1).Value = "SCHEDULE OF INSPECTIONS  —  BS 7671 Reg 642";
            ws.Range(1, 1, 1, 3).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[,] items =
            {
                { "1.0",  "Connection of conductors",                                          "" },
                { "2.0",  "Identification of conductors",                                      "" },
                { "3.0",  "Routing of cables in safe zones / mechanical protection",           "" },
                { "4.0",  "Selection of conductors for current-carrying capacity / VD",        "" },
                { "5.0",  "Erection methods / IP ratings",                                     "" },
                { "6.0",  "Connection of accessories / equipment",                             "" },
                { "7.0",  "Presence of fire barriers / suitable seals / protection ag. fire",  "" },
                { "8.0",  "Methods of basic protection — insulation of live parts",            "" },
                { "9.0",  "Methods of basic protection — barriers / enclosures (IPXXB)",       "" },
                { "10.0", "Methods of fault protection — automatic disconnection of supply",   "" },
                { "11.0", "Earthing arrangements (TN-S / TN-C-S / TT)",                        "" },
                { "12.0", "Main protective bonding conductors — water / gas / structural",     "" },
                { "13.0", "Supplementary equipotential bonding (where required)",              "" },
                { "14.0", "Earth electrode resistance (TT systems)",                           "" },
                { "15.0", "Disconnection times verified per Table 41.1",                       "" },
                { "16.0", "RCDs — operating current / time per Reg 415",                       "" },
                { "17.0", "SPDs — Type 1 / Type 2 (Reg 443 / 534) where required",             "" },
                { "18.0", "Labelling — Caution notices / RCD test / circuit identification",   "" },
                { "19.0", "Distribution boards — schedule fixed inside",                       "" },
                { "20.0", "Suitability of equipment for environment (BS EN 60529 IP rating)",  "" }
            };
            ws.Cell(2, 1).Value = "Item"; ws.Cell(2, 2).Value = "Description"; ws.Cell(2, 3).Value = "✓ / ✗ / N/A";
            ws.Range(2, 1, 2, 3).Style.Font.Bold = true;
            ws.Range(2, 1, 2, 3).Style.Fill.BackgroundColor = XLColor.LightGray;
            int row = 3;
            for (int i = 0; i < items.GetLength(0); i++)
            {
                ws.Cell(row, 1).Value = items[i, 0];
                ws.Cell(row, 2).Value = items[i, 1];
                ws.Cell(row, 3).Value = items[i, 2];
                ws.Range(row, 1, row, 3).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.Column(2).Width = 60;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        }

        private static void WriteCircuitDetailsSheet(IXLWorksheet ws, List<CircuitAuditResult> rows)
        {
            ws.Cell(1, 1).Value = "SCHEDULE OF CIRCUIT DETAILS";
            ws.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 9).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[] hdr = { "Panel", "Circuit", "Description", "OCPD",
                             "Cable CSA", "CPC CSA", "Length (m)", "Zs design (Ω)", "Verdict" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(x => x.PanelName).ThenBy(x => x.CircuitTag))
            {
                ws.Cell(row, 1).Value = r.PanelName;
                ws.Cell(row, 2).Value = r.CircuitTag;
                ws.Cell(row, 3).Value = r.LoadName;
                ws.Cell(row, 4).Value = "—";  // OCPD label — engineer fills
                ws.Cell(row, 5).Value = "—";
                ws.Cell(row, 6).Value = "—";
                ws.Cell(row, 7).Value = "—";
                ws.Cell(row, 8).Value = r.ZsActualOhm;
                ws.Cell(row, 9).Value = r.Verdict;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }

        private static void WriteTestResultsSheet(IXLWorksheet ws, List<CircuitAuditResult> rows)
        {
            ws.Cell(1, 1).Value = "SCHEDULE OF TEST RESULTS  —  BS 7671 Appendix 6";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            ws.Cell(2, 1).Value = "Tester to fill measured columns. Design columns pre-filled from STING audit.";
            ws.Range(2, 1, 2, 10).Merge().Style.Font.Italic = true;

            string[] hdr = { "Panel", "Circuit", "Insulation R L-L (MΩ)", "Insulation R L-E (MΩ)",
                             "Continuity R1+R2 (Ω)", "Polarity",
                             "Zs design (Ω)", "Zs measured (Ω)",
                             "RCD ½×IΔn (ms)", "RCD 1×IΔn (ms)" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(3, i + 1).Value = hdr[i];
                ws.Cell(3, i + 1).Style.Font.Bold = true;
                ws.Cell(3, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 4;
            foreach (var r in rows.OrderBy(x => x.PanelName).ThenBy(x => x.CircuitTag))
            {
                ws.Cell(row, 1).Value = r.PanelName;
                ws.Cell(row, 2).Value = r.CircuitTag;
                // Empty measured cells — tester writes ≥1 MΩ result + measured Zs.
                ws.Cell(row, 7).Value = r.ZsActualOhm;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }
    }
}
