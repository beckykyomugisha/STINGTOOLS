// ══════════════════════════════════════════════════════════════════════════
//  CostControlCommands.cs — P4 cost-control closers.
//
//  Closes the "end to end from the panel" gaps the engines couldn't yet
//  reach on their own:
//
//   • PaymentCert_SetProgress     — set % complete per BOQ section (stamps
//     ASS_PMT_PCT_COMPLETE_NR on its elements) so interim certs + EVM have a
//     real input rather than everything reading 0.
//   • PaymentCert_ExportDoc       — render a numbered interim certificate as a
//     formatted XLSX (SOV table + retention/MOS/previous/net/VAT/payable +
//     signature block). The certificate document a QS actually issues.
//   • Cost_AnticipatedFinalCost   — anticipated-final-cost report: modelled
//     works + manual/PS allowances + agreed variations + pending variations
//     → AFC vs budget, on screen + XLSX.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.PaymentCert;
using StingTools.Core.Variation;
using StingTools.Select;   // StingListPicker
using StingTools.UI;       // StingResultPanel

namespace StingTools.Commands.Cost
{
    // ══════════════════════════════════════════════════════════════════════
    //  P4.1 — Set % complete per BOQ section
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaymentCertSetProgressCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq.Sections.Count == 0)
                {
                    StingResultPanel.Create("Set % complete")
                        .AddSection("NO DATA")
                        .Text("BOQ has no sections — build the BOQ first.")
                        .Show();
                    return Result.Cancelled;
                }

                // P2.2 — inline-form gate. When the BOQ panel supplied PmtSection +
                // PmtPercent ExtraParams, skip the picker chain and apply directly.
                // Falls back to the pickers (below) when absent so ribbon / other
                // callers keep working.
                string fSection = StingCommandHandler.GetExtraParam("PmtSection");
                string fPercent = StingCommandHandler.GetExtraParam("PmtPercent");
                List<StingListPicker.ListItem> pickedSecs;
                double pct;
                if (!string.IsNullOrEmpty(fSection)
                    && double.TryParse(fPercent, NumberStyles.Any, CultureInfo.InvariantCulture, out pct))
                {
                    pct = Math.Max(0, Math.Min(100, pct));
                    IEnumerable<BOQSection> chosen = string.Equals(fSection, "ALL", StringComparison.OrdinalIgnoreCase)
                        ? boq.Sections
                        : boq.Sections.Where(s =>
                            string.Equals(s.NRM2Section, fSection, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s.Name, fSection, StringComparison.OrdinalIgnoreCase));
                    pickedSecs = chosen.Select(s => new StingListPicker.ListItem { Tag = s }).ToList();
                    if (pickedSecs.Count == 0) return Result.Cancelled;
                }
                else
                {
                    // Pick section(s).
                    var secItems = boq.Sections.Select(s => new StingListPicker.ListItem
                    {
                        Label = string.IsNullOrEmpty(s.NRM2Section) ? s.Name : $"§{s.NRM2Section}  {s.Name}",
                        Detail = $"{s.Items.Count} items · UGX {s.TotalUGX:N0}",
                        Tag = s
                    }).ToList();
                    pickedSecs = StingListPicker.Show("STING — Set % complete",
                        "Pick the section(s) to update, then a percentage.", secItems, allowMultiSelect: true);
                    if (pickedSecs == null || pickedSecs.Count == 0) return Result.Cancelled;

                    // Pick a percentage.
                    var pctItems = new[] { 0, 5, 10, 20, 25, 30, 40, 50, 60, 70, 75, 80, 90, 95, 100 }
                        .Select(p => new StingListPicker.ListItem { Label = $"{p}%", Tag = (double)p }).ToList();
                    var pickedPct = StingListPicker.Show("STING — % complete",
                        "Percentage complete to apply to the selected section(s).", pctItems, allowMultiSelect: false);
                    if (pickedPct == null || pickedPct.Count == 0 || !(pickedPct[0].Tag is double pctPicked))
                        return Result.Cancelled;
                    pct = pctPicked;
                }

                int stamped = 0, missing = 0;
                using (var t = new Transaction(doc, "STING — set % complete"))
                {
                    t.Start();
                    foreach (var pi in pickedSecs)
                    {
                        if (!(pi.Tag is BOQSection sec)) continue;
                        foreach (var item in sec.Items)
                        {
                            if (item.RevitElementId <= 0) continue;
                            Element el;
                            try { el = doc.GetElement(new ElementId(item.RevitElementId)); }
                            catch (Exception ex) { StingLog.WarnRateLimited("PctComplete.GetEl", $"GetElement({item.RevitElementId}): {ex.Message}"); continue; }
                            if (el == null) continue;
                            var p = el.LookupParameter(ParamRegistry.PMT_PCT_COMPLETE_NR);
                            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) { missing++; continue; }
                            try { p.Set(pct); stamped++; }
                            catch (Exception ex) { StingLog.WarnRateLimited("PctComplete.Set", $"set %-complete on {item.RevitElementId}: {ex.Message}"); missing++; }
                        }
                    }
                    t.Commit();
                }

                string note = missing > 0 && stamped == 0
                    ? "No element was stamped — bind ASS_PMT_PCT_COMPLETE_NR via Load Params first."
                    : (missing > 0 ? $"{missing} element(s) skipped (param not bound / read-only)." : "");
                var rp = StingResultPanel.Create("Progress set")
                    .AddSection("RESULT")
                    .Metric("% complete applied", $"{pct:0}%")
                    .Metric("Elements stamped", stamped.ToString())
                    .Metric("Sections", pickedSecs.Count.ToString());
                if (!string.IsNullOrEmpty(note)) rp.Text(note);
                rp.Text("Issue Cert + Calculate EVM now read this back.");
                rp.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PaymentCert_SetProgress", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  P4.1 — Interim certificate document (XLSX)
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaymentCertExportDocCommand : IExternalCommand
    {
        private static readonly XLColor Navy = XLColor.FromArgb(26, 58, 92);
        private static readonly XLColor Head = XLColor.FromArgb(46, 94, 142);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = PaymentCertEngine.ListCerts(doc);
                if (paths.Count == 0)
                {
                    StingResultPanel.Create("Cert document")
                        .AddSection("NO CERTS")
                        .Text("No certs found — issue one first (★ Issue Cert).")
                        .Show();
                    return Result.Cancelled;
                }
                // P0.3 — inline-form gate: when the BOQ panel supplied CertPath,
                // render that cert without a popup. Falls back to the modal picker.
                PaymentCertificate cert;
                string fPath = UI.StingCommandHandler.GetExtraParam("CertPath");
                if (!string.IsNullOrEmpty(fPath) && File.Exists(fPath))
                {
                    cert = PaymentCertEngine.Load(fPath);
                    if (cert == null) { message = "Failed to load certificate."; return Result.Failed; }
                }
                else
                {
                    var certs = paths.Select(PaymentCertEngine.Load).Where(c => c != null)
                        .OrderByDescending(c => c.CertNumber).ToList();
                    var items = certs.Select(c => new StingListPicker.ListItem
                    {
                        Label = $"Cert #{c.CertNumber}  ({c.Status})",
                        Detail = $"{c.ContractRef} — {c.Currency} {c.TotalPayable:N0} — {c.ValuationDate:yyyy-MM-dd}",
                        Tag = c
                    }).ToList();
                    var picked = StingListPicker.Show("STING — Export certificate document",
                        "Pick the certificate to render as XLSX.", items, allowMultiSelect: false);
                    if (picked == null || picked.Count == 0 || !(picked[0].Tag is PaymentCertificate pc))
                        return Result.Cancelled;
                    cert = pc;
                }

                string path = OutputLocationHelper.GetTimestampedPath(
                    doc, $"STING_PaymentCert_{cert.CertNumber:D3}", ".xlsx");
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet($"Cert {cert.CertNumber}");
                    BuildCertSheet(ws, cert);
                    wb.SaveAs(path);
                }

                StingLog.Info($"Payment cert #{cert.CertNumber} document exported: {path}");
                StingResultPanel.Create("Certificate document")
                    .SetSubtitle($"Interim Certificate No. {cert.CertNumber} exported")
                    .SetCsvPath(path)
                    .AddSection("EXPORT")
                    .Metric("Payable", $"{cert.Currency} {cert.TotalPayable:N2}")
                    .Text($"Path: {path}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PaymentCert_ExportDoc", ex); message = ex.Message; return Result.Failed; }
        }

        private void BuildCertSheet(IXLWorksheet ws, PaymentCertificate cert)
        {
            ws.Cell(1, 1).Value = $"INTERIM PAYMENT CERTIFICATE No. {cert.CertNumber}";
            ws.Range(1, 1, 1, 8).Merge().Style.Fill.SetBackgroundColor(Navy)
                .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(15);

            int r = 3;
            void Meta(string k, string v) { ws.Cell(r, 1).Value = k; ws.Cell(r, 1).Style.Font.SetBold();
                ws.Cell(r, 3).Value = v; r++; }
            Meta("Project", cert.ProjectName);
            Meta("Contract ref", cert.ContractRef);
            Meta("Contract form", cert.Form.ToString());
            Meta("Employer", cert.EmployerName);
            Meta("Contractor", cert.ContractorName);
            Meta("Valuation date", cert.ValuationDate.ToString("dd MMM yyyy"));
            Meta("Status", cert.Status.ToString());
            Meta("Currency", cert.Currency);
            r++;

            // SOV table
            string[] cols = { "Section", "Description", "Contract value", "% complete",
                "Gross to date", "Previously certified", "Materials on site", "Gross this cert" };
            for (int c = 0; c < cols.Length; c++) ws.Cell(r, c + 1).Value = cols[c];
            ws.Range(r, 1, r, cols.Length).Style.Fill.SetBackgroundColor(Head)
                .Font.SetFontColor(XLColor.White).Font.SetBold();
            r++;
            foreach (var l in cert.Lines)
            {
                ws.Cell(r, 1).Value = l.Section;
                ws.Cell(r, 2).Value = l.Description;
                ws.Cell(r, 3).Value = l.ContractValue;
                ws.Cell(r, 4).Value = l.PercentComplete / 100.0; ws.Cell(r, 4).Style.NumberFormat.Format = "0%";
                ws.Cell(r, 5).Value = Math.Round(l.ContractValue * l.PercentComplete / 100.0, 2);
                ws.Cell(r, 6).Value = l.PreviouslyCertified;
                ws.Cell(r, 7).Value = l.MaterialsOnSite;
                ws.Cell(r, 8).Value = l.GrossThisCert;
                r++;
            }
            ws.Range(r - cert.Lines.Count, 3, r - 1, 8).Style.NumberFormat.Format = "#,##0";
            r++;

            // Summary
            void Sum(string k, double v, bool bold = false)
            {
                ws.Cell(r, 6).Value = k;
                ws.Cell(r, 8).Value = v; ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0";
                if (bold) ws.Range(r, 6, r, 8).Style.Font.SetBold();
                r++;
            }
            Sum("Gross valuation", cert.GrossValuation, true);
            Sum($"Retention ({cert.EffectiveRetentionPercent:0.##}%)", -cert.RetentionAmount);
            Sum("Other deductions", -cert.OtherDeductions);
            Sum("Net this certificate", cert.NetThisCert, true);
            Sum($"VAT ({cert.VatPercent:0.##}%)", cert.VatAmount);
            ws.Cell(r, 6).Value = "TOTAL PAYABLE";
            ws.Cell(r, 8).Value = cert.TotalPayable; ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0";
            ws.Range(r, 6, r, 8).Style.Fill.SetBackgroundColor(Navy).Font.SetFontColor(XLColor.White).Font.SetBold();
            r += 3;

            // Signature block
            ws.Cell(r, 1).Value = "Certified by (Employer's Agent / PM):";
            ws.Cell(r, 4).Value = cert.SignedByEmployer;
            ws.Cell(r, 6).Value = cert.EmployerSignedDate?.ToString("dd MMM yyyy") ?? "____________";
            r += 2;
            ws.Cell(r, 1).Value = "Agreed by (Contractor):";
            ws.Cell(r, 4).Value = cert.SignedByContractor;
            ws.Cell(r, 6).Value = cert.ContractorSignedDate?.ToString("dd MMM yyyy") ?? "____________";

            ws.Column(2).Width = 42;
            ws.Columns(3, 8).AdjustToContents();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  P4.4 — Anticipated final cost / cost report
    // ══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostAnticipatedFinalCostCommand : IExternalCommand
    {
        private static readonly XLColor Navy = XLColor.FromArgb(26, 58, 92);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                string ccy = boq.Currency ?? "UGX";

                double modeled = boq.ModeledTotalUGX;
                double provAndManual = boq.ProvTotalUGX;          // PS + manual additions (incl. dayworks)
                double subtotal = boq.SubtotalUGX;
                double grand = boq.GrandTotalUGX;                 // grossed up by prelims/cont/OH&P
                double budget = boq.ProjectBudgetUGX;

                // Variations — split agreed vs pending.
                string contractRef = doc.ProjectInformation?.Number ?? "";
                var vos = VariationEngine.ListVariations(doc)
                    .Select(VariationEngine.Load).Where(v => v != null).ToList();
                double agreedVo = vos.Where(v => v.Status == VariationStatus.Approved
                                              || v.Status == VariationStatus.Incorporated)
                                     .Sum(v => v.TotalValue);
                double pendingVo = vos.Where(v => v.Status == VariationStatus.Draft
                                               || v.Status == VariationStatus.Submitted
                                               || v.Status == VariationStatus.Reviewed)
                                      .Sum(v => v.TotalValue);

                // B.3 — anchor AFC on a FROZEN contract sum, not the live BOQ
                // grand total. The live grand already reflects the model changes
                // the agreed VOs were minted from, so (grand + agreedVo) double-
                // counts. The contract sum is the earliest non-draft payment
                // certificate's Schedule-of-Values total; absent any cert, fall
                // back to the live grand total and flag the assumption.
                var baseCert = PaymentCertEngine.ListCerts(doc)
                    .Select(PaymentCertEngine.Load)
                    .Where(c => c != null && c.Status != PaymentCertStatus.Superseded
                                          && c.Status != PaymentCertStatus.Draft
                                          && c.Lines != null && c.Lines.Count > 0)
                    .OrderBy(c => c.CertNumber)
                    .FirstOrDefault();
                bool haveBaseline = baseCert != null;
                // PM-2 — unify the contract-sum source with the Final Account + EVM:
                // the FROZEN Award baseline (COST_CONTRACT_SUM_UGX via ContractSumResolver)
                // wins, then the earliest cert's SOV, then the live grand total.
                double contractSum; string contractSumBasis;
                if (TagConfig.GetConfigDouble("COST_CONTRACT_SUM_UGX", 0.0) > 0)
                {
                    contractSum = ContractSumResolver.ResolveBase(doc, boq, out contractSumBasis);
                }
                else if (haveBaseline)
                {
                    contractSum = Math.Round(baseCert.Lines.Sum(l => l.ContractValue), 0);
                    contractSumBasis = $"Cert #{baseCert.CertNumber} SOV (no frozen baseline)";
                }
                else
                {
                    // CA-2 — ONE BASIS: the fallback must be NET of VAT, like the
                    // frozen-baseline and cert-SOV paths above. Using the
                    // VAT-inclusive grand here made the contract-sum definition
                    // flip basis depending on which source resolved.
                    contractSum = boq.NetTotalExVatUGX;
                    contractSumBasis = "live BOQ net-of-VAT total — no frozen baseline / cert, assumption";
                }

                // G2 — provisional-sum movement (Σ reconciled actual − original)
                // from the reconciliation trail. The baseline carries PS at their
                // ORIGINAL allowances, so adding the movement (a delta, not the
                // full actual) lands the AFC on PS actuals without double-counting.
                double psMovement = BoqProvisionalTrail.MovementUGX(doc);

                // PM-3 — fluctuations (index-linked) now feed the AFC too, not only
                // the Final Account. Computed via Fluctuations_Compute → COST_FLUCTUATIONS_UGX.
                double fluctuations = TagConfig.GetConfigDouble("COST_FLUCTUATIONS_UGX", 0.0);

                // PM-3 — priced dayworks reach the AFC on the same basis as the
                // Final Account: ONLY sheets not attached to a variation, since an
                // attached sheet is already inside agreedVo / pendingVo via its
                // VariationItem. 0 when no dayworks are recorded.
                double dayworks = Core.Variation.DayworkEngine.UnattachedPricedTotal(doc);

                double afcAgreedOnly = Math.Round(contractSum + agreedVo + psMovement + fluctuations + dayworks, 0);
                double afc = Math.Round(contractSum + agreedVo + pendingVo + psMovement + fluctuations + dayworks, 0);
                double variance = budget > 0 ? budget - afc : 0;

                // On-screen summary.
                var builder = StingResultPanel.Create("Anticipated Final Cost")
                    .SetSubtitle($"{boq.ProjectName} · {DateTime.Now:dd MMM yyyy} · {ccy}")
                    .AddSection("BILL OF QUANTITIES")
                    .Metric("Modelled works", $"{ccy} {modeled:N0}")
                    .Metric("Manual / PS allowances", $"{ccy} {provAndManual:N0}")
                    .Metric("Subtotal", $"{ccy} {subtotal:N0}")
                    .Metric($"+ Prelims/Cont/OH&P", $"{ccy} {grand - subtotal:N0}")
                    .Metric("BOQ grand total", $"{ccy} {grand:N0}")
                    .AddSection("VARIATIONS")
                    .Metric("Agreed variations", $"{ccy} {agreedVo:N0}")
                    .Metric("Pending variations", $"{ccy} {pendingVo:N0}")
                    .Metric("Variation count", $"{vos.Count}")
                    .Metric("Fluctuations (index-linked)", $"{ccy} {fluctuations:N0}")
                    .Metric("Priced dayworks (unattached)", $"{ccy} {dayworks:N0}")
                    .AddSection("ANTICIPATED FINAL COST")
                    .Metric("Contract sum (baseline)", $"{ccy} {contractSum:N0}")
                    .Metric("Baseline basis", contractSumBasis)
                    .Metric("Provisional-sum movement", $"{(psMovement >= 0 ? "+" : "")}{ccy} {psMovement:N0}")
                    .Metric("AFC (agreed only)", $"{ccy} {afcAgreedOnly:N0}")
                    .Metric("AFC (incl. pending)", $"{ccy} {afc:N0}")
                    .Metric("Budget", budget > 0 ? $"{ccy} {budget:N0}" : "— not set —")
                    .Metric("Variance vs budget", budget > 0 ? $"{(variance >= 0 ? "+" : "")}{ccy} {variance:N0}" : "—");

                if (vos.Count > 0)
                {
                    var rows = vos.OrderBy(v => v.Number).Select(v => new[]
                    {
                        v.Number ?? "", v.Status.ToString(), v.Kind.ToString(),
                        $"{ccy} {v.TotalValue:N0}", v.Title ?? ""
                    }).ToList();
                    builder.AddSection("VARIATION REGISTER")
                        .Table(new[] { "No.", "Status", "Kind", "Value", "Title" }, rows);
                }
                builder.Show();

                // XLSX export.
                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_AnticipatedFinalCost", ".xlsx");
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet("Anticipated Final Cost");
                    ws.Cell(1, 1).Value = $"ANTICIPATED FINAL COST — {boq.ProjectName}";
                    ws.Range(1, 1, 1, 3).Merge().Style.Fill.SetBackgroundColor(Navy)
                        .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(14);
                    int r = 3;
                    void Line(string k, double v, bool bold = false)
                    {
                        ws.Cell(r, 1).Value = k; ws.Cell(r, 3).Value = v;
                        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0";
                        if (bold) ws.Range(r, 1, r, 3).Style.Font.SetBold();
                        r++;
                    }
                    Line("Modelled works", modeled);
                    Line("Manual / PS allowances", provAndManual);
                    Line("Subtotal", subtotal, true);
                    Line("Prelims / Contingency / OH&P", grand - subtotal);
                    Line("BOQ grand total (live)", grand, true);
                    r++;
                    Line($"Contract sum baseline [{contractSumBasis}]", contractSum, true);
                    Line("Provisional-sum movement", psMovement);
                    Line("Agreed variations", agreedVo);
                    Line("Pending variations", pendingVo);
                    // PM-3 — fluctuations were computed into the AFC but never
                    // printed here, so the exported lines did not sum to the AFC
                    // figure below them whenever fluctuations were non-zero.
                    Line("Fluctuations (index-linked)", fluctuations);
                    Line("Priced dayworks (unattached)", dayworks);
                    r++;
                    Line("AFC (agreed only)", afcAgreedOnly, true);
                    Line("AFC (incl. pending)", afc, true);
                    if (budget > 0) { Line("Budget", budget); Line("Variance vs budget", variance, true); }
                    ws.Column(1).Width = 36; ws.Column(3).Width = 18;
                    wb.SaveAs(path);
                }
                StingLog.Info($"Anticipated final cost report exported: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Cost_AnticipatedFinalCost", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
