// ══════════════════════════════════════════════════════════════════════════
//  PaymentCertCommands.cs — Payment certificate commands (P5.1).
//
//  PaymentCert_Issue    — build a draft cert from BOQ + SOV + % complete.
//  PaymentCert_Approve  — flip status Draft → Issued → Agreed; stamp
//                         contractor / employer signature names.
//  PaymentCert_Register — produce a CSV register of all certs for a
//                         contract showing gross, retention, payable
//                         and cumulative paid.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.PaymentCert;
using StingTools.Select;

namespace StingTools.Commands.Cost
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaymentCertIssueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Contract reference defaults to project number.
                string contractRef = doc.ProjectInformation?.Number ?? "DEFAULT";

                // Build SOV from current BOQ snapshot.
                var boq = BOQCostManager.BuildBOQDocument(doc);
                var sov = PaymentCertEngine.SovFromSnapshot(boq);
                if (sov.Count == 0)
                {
                    TaskDialog.Show("STING Payment Cert", "BOQ has no sections — build the BOQ first.");
                    return Result.Cancelled;
                }

                // Pull live % complete from elements (where bound) — sum
                // ASS_PMT_PCT_COMPLETE_NR weighted by ContractValue.
                AggregatePercentComplete(doc, sov);

                // Pick contract form.
                ContractForm form = PickContractForm();

                var cert = PaymentCertEngine.CreateDraft(doc, contractRef, form, sov);
                cert.EmployerName = doc.ProjectInformation?.OrganizationName ?? "";
                cert.ContractorName = ParameterHelpers.GetString(doc.ProjectInformation,
                    "PRJ_ORG_LEAD_APPOINTED_PARTY_TXT") ?? "";

                string path = PaymentCertEngine.Save(doc, cert);

                // Stamp elements with cert number + date inside a transaction.
                var idsBySection = BuildSectionElementMap(boq);
                using (var t = new Transaction(doc, "STING — payment cert element stamp"))
                {
                    t.Start();
                    int stamped = PaymentCertEngine.StampElements(doc, cert, idsBySection);
                    t.Commit();
                    StingLog.Info($"Payment cert {cert.CertNumber}: stamped {stamped} elements.");
                }

                TaskDialog.Show("STING — Payment cert issued",
                    $"Cert #{cert.CertNumber} ({cert.ContractRef}, {cert.Form})\n\n" +
                    $"Gross:      {cert.Currency} {cert.GrossValuation:N2}\n" +
                    $"Retention:  {cert.Currency} {cert.RetentionAmount:N2}   ({cert.EffectiveRetentionPercent}%)\n" +
                    $"Deductions: {cert.Currency} {cert.OtherDeductions:N2}\n" +
                    $"Net:        {cert.Currency} {cert.NetThisCert:N2}\n" +
                    $"VAT:        {cert.Currency} {cert.VatAmount:N2}\n" +
                    $"Payable:    {cert.Currency} {cert.TotalPayable:N2}\n\n" +
                    $"Path: {Path.GetFileName(path)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PaymentCert_Issue", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void AggregatePercentComplete(Document doc, List<SovLine> sov)
        {
            try
            {
                // For each SOV line (= NRM2 section), sum ASS_PMT_PCT_COMPLETE_NR
                // weighted by element TotalUGX so the line-level % matches the
                // £-weighted average completion across all elements in the section.
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var weighted = new Dictionary<string, (double weightSum, double valueSum)>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (Element el in col)
                {
                    string section = ParameterHelpers.GetString(el, "ASS_BOQ_SECTION_NAME");
                    if (string.IsNullOrEmpty(section)) continue;
                    Parameter p = el.LookupParameter(ParamRegistry.PMT_PCT_COMPLETE_NR);
                    if (p == null || !p.HasValue) continue;
                    double pct = p.AsDouble();
                    if (pct <= 0) continue;
                    Parameter tot = el.LookupParameter("CST_MODELED_TOTAL_UGX");
                    double total = (tot != null && tot.HasValue) ? tot.AsDouble() : 0;
                    if (total <= 0) total = 1;
                    if (!weighted.TryGetValue(section, out var cur))
                        cur = (0, 0);
                    cur.weightSum += pct * total;
                    cur.valueSum += total;
                    weighted[section] = cur;
                }

                foreach (var line in sov)
                {
                    if (weighted.TryGetValue(line.Section, out var w) && w.valueSum > 0)
                        line.PercentComplete = Math.Round(w.weightSum / w.valueSum, 2);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AggregatePercentComplete: {ex.Message}"); }
        }

        private static Dictionary<string, IList<long>> BuildSectionElementMap(BOQDocument boq)
        {
            var map = new Dictionary<string, IList<long>>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in boq.Sections)
            {
                string key = string.IsNullOrEmpty(section.NRM2Section) ? section.Name : section.NRM2Section;
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<long>();
                    map[key] = list;
                }
                foreach (var item in section.Items)
                    if (item.RevitElementId > 0) list.Add(item.RevitElementId);
            }
            return map;
        }

        private static ContractForm PickContractForm()
        {
            var items = new List<StingListPicker.ListItem>
            {
                new StingListPicker.ListItem { Label = "NEC4 ECC", Tag = ContractForm.NEC4 },
                new StingListPicker.ListItem { Label = "JCT 2024 Standard Building Contract", Tag = ContractForm.JCT2024 },
                new StingListPicker.ListItem { Label = "FIDIC Red Book 2017", Tag = ContractForm.FIDIC2017Red }
            };
            var picked = StingListPicker.Show("STING — Contract form",
                "Pick the contract form so the cert uses the right wording.",
                items, allowMultiSelect: false);
            if (picked != null && picked.Count > 0 && picked[0].Tag is ContractForm f) return f;
            return ContractForm.NEC4;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaymentCertApproveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = PaymentCertEngine.ListCerts(doc);
                if (paths.Count == 0)
                {
                    TaskDialog.Show("STING Payment Cert", "No payment certs found.");
                    return Result.Cancelled;
                }
                var certs = paths.Select(PaymentCertEngine.Load).Where(c => c != null).ToList();
                var draftItems = certs
                    .Where(c => c.Status == PaymentCertStatus.Draft || c.Status == PaymentCertStatus.Issued)
                    .Select(c => new StingListPicker.ListItem
                    {
                        Label = $"Cert #{c.CertNumber}  ({c.Status})",
                        Detail = $"{c.ContractRef} — {c.Currency} {c.TotalPayable:N0} — {c.ValuationDate:yyyy-MM-dd}",
                        Tag = c
                    }).ToList();
                if (draftItems.Count == 0)
                {
                    TaskDialog.Show("STING Payment Cert", "No certs are in a state that can be approved.");
                    return Result.Cancelled;
                }
                var picked = StingListPicker.Show("STING — Approve payment cert",
                    "Pick the certificate to advance.", draftItems, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;
                var cert = picked[0].Tag as PaymentCertificate;
                if (cert == null) return Result.Cancelled;

                // Advance state machine — Draft → Issued → Agreed.
                cert.Status = cert.Status == PaymentCertStatus.Draft
                    ? PaymentCertStatus.Issued
                    : PaymentCertStatus.Agreed;
                if (cert.Status == PaymentCertStatus.Issued)
                {
                    cert.IssuedDate = DateTime.UtcNow;
                    cert.SignedByEmployer = Environment.UserName ?? "";
                    cert.EmployerSignedDate = DateTime.UtcNow;
                }
                else
                {
                    cert.SignedByContractor = Environment.UserName ?? "";
                    cert.ContractorSignedDate = DateTime.UtcNow;
                }

                // Re-save in place by deleting the old file and re-writing.
                string oldPath = picked[0].Tag is PaymentCertificate c ? FindPathForCert(doc, c) : null;
                if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath)) File.Delete(oldPath);
                string newPath = PaymentCertEngine.Save(doc, cert);

                TaskDialog.Show("STING — Cert advanced",
                    $"Cert #{cert.CertNumber} is now {cert.Status}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PaymentCert_Approve", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string FindPathForCert(Document doc, PaymentCertificate cert)
        {
            try
            {
                return PaymentCertEngine.ListCerts(doc, cert.ContractRef)
                    .FirstOrDefault(p =>
                    {
                        var c = PaymentCertEngine.Load(p);
                        return c != null && c.Id == cert.Id;
                    });
            }
            catch { return null; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaymentCertRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = PaymentCertEngine.ListCerts(doc);
                if (paths.Count == 0)
                {
                    TaskDialog.Show("STING Payment Cert", "No certs found.");
                    return Result.Cancelled;
                }
                var certs = paths.Select(PaymentCertEngine.Load).Where(c => c != null)
                    .OrderBy(c => c.ContractRef).ThenBy(c => c.CertNumber).ToList();

                string outDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "payment_certs");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir,
                    $"payment_cert_register_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                using (var sw = new StreamWriter(outPath))
                {
                    sw.WriteLine("Contract,CertNo,ValuationDate,Status,Form,Currency,Gross,RetentionPct,Retention,Deductions,Net,VAT,Payable,SignedByEmployer,SignedByContractor");
                    foreach (var c in certs)
                    {
                        sw.WriteLine(string.Join(",", new[]
                        {
                            Q(c.ContractRef),
                            c.CertNumber.ToString(CultureInfo.InvariantCulture),
                            c.ValuationDate.ToString("yyyy-MM-dd"),
                            c.Status.ToString(),
                            c.Form.ToString(),
                            c.Currency,
                            c.GrossValuation.ToString("F2", CultureInfo.InvariantCulture),
                            c.EffectiveRetentionPercent.ToString("F2", CultureInfo.InvariantCulture),
                            c.RetentionAmount.ToString("F2", CultureInfo.InvariantCulture),
                            c.OtherDeductions.ToString("F2", CultureInfo.InvariantCulture),
                            c.NetThisCert.ToString("F2", CultureInfo.InvariantCulture),
                            c.VatAmount.ToString("F2", CultureInfo.InvariantCulture),
                            c.TotalPayable.ToString("F2", CultureInfo.InvariantCulture),
                            Q(c.SignedByEmployer),
                            Q(c.SignedByContractor)
                        }));
                    }
                }
                TaskDialog.Show("STING — Register exported",
                    $"{certs.Count} certificate(s) exported to:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PaymentCert_Register", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"")) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
