// ══════════════════════════════════════════════════════════════════════════
//  FinalAccountCommands.cs — WP4a Tool 2: Final-Account Reconciliation.
//
//  A true signed reconciliation statement (not just an anticipated cost):
//
//    Contract Sum (WP1 canonical GrandTotal, frozen at award, VAT-incl)
//      − provisional / PC-sum allowances carried in the contract
//      + reconciled provisional / PC-sum actuals      (BoqProvisional trail)
//      ± agreed variations (Approved + Incorporated)  (VariationEngine)
//      ± fluctuations (COST_FLUCTUATIONS_UGX, 0 if none)
//      = Final Account
//
//  Also reports the AS-BUILT remeasure (the live canonical GrandTotal) and its
//  variance against the frozen Contract Sum (BOQPricingMode.AsBuilt basis).
//
//  Persists to <project>/_BIM_COORD/final_account.json (additive, re-openable)
//  and exports a formatted XLSX: waterfall + variations annexure + provisional
//  annexure, stamped with the QS sign-off (draft until signed via BoqSignOff).
//
//  Command tag: FinalAccount_Reconcile.
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
using Newtonsoft.Json;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Variation;
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    // ── Persistence model ─────────────────────────────────────────────────

    public class FinalAccountStatement
    {
        public string SchemaVersion = "1.0";
        public string ProjectName = "";
        public string GeneratedDate = "";        // yyyy-MM-dd HH:mm
        public string Currency = "UGX";

        public double ContractSumUGX;             // frozen at award (VAT-incl canonical)
        public string ContractSumSource = "";     // how it was resolved

        public double ProvisionalAllowancesUGX;   // Σ original sum of reconciled PS/PC records
        public double ProvisionalActualsUGX;      // Σ reconciled actual
        public double ProvisionalMovementUGX;     // actuals − allowances

        public double AgreedVariationsUGX;
        public int    AgreedVariationCount;

        public double FluctuationsUGX;

        public double FinalAccountUGX;            // the signed bottom line

        public double AsBuiltRemeasureUGX;        // live canonical GrandTotal (as-built basis)
        public double AsBuiltVarianceUGX;         // asBuilt − contractSum

        public string SignedBy = "";
        public string SignedRole = "";
        public string SignedDate = "";

        public List<FinalAccountVariationRow> Variations = new List<FinalAccountVariationRow>();
        public List<FinalAccountProvisionalRow> Provisionals = new List<FinalAccountProvisionalRow>();
    }

    public class FinalAccountVariationRow
    {
        public string Number = "";
        public string Kind = "";
        public string Status = "";
        public double ValueUGX;
        public string ApprovedBy = "";
        public string ApprovalDate = "";
    }

    public class FinalAccountProvisionalRow
    {
        public string Description = "";
        public double OriginalSumUGX;
        public double? ReconciledActualUGX;
        public double MovementUGX;
        public string Status = "";
    }

    internal static class FinalAccountStore
    {
        private static string PathFor(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "final_account.json");
            }
            catch { return null; }
        }

        public static FinalAccountStatement Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || !File.Exists(p)) return null;
                return JsonConvert.DeserializeObject<FinalAccountStatement>(File.ReadAllText(p));
            }
            catch (Exception ex) { StingLog.Warn($"FinalAccountStore.Load: {ex.Message}"); return null; }
        }

        public static void Save(Document doc, FinalAccountStatement s)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || s == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"FinalAccountStore.Save: {ex.Message}"); }
        }
    }

    // ── Command ────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FinalAccountReconcileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // As-built remeasure = the live canonical bill.
                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq == null)
                {
                    TaskDialog.Show("Final Account", "Could not build the BOQ for reconciliation.");
                    return Result.Cancelled;
                }
                string ccy = boq.Currency ?? "UGX";
                double asBuilt = boq.GrandTotalUGX;

                // 1. Contract Sum at award (frozen). Priority: an explicit
                //    COST_CONTRACT_SUM_UGX → a previously saved statement → the
                //    most relevant snapshot (tender/award/contract/DD) → the live
                //    canonical total (clearly flagged as not yet frozen).
                var prior = FinalAccountStore.Load(doc);
                double contractSum = 0; string contractSrc = "";
                double cfg = TagConfig.GetConfigDouble("COST_CONTRACT_SUM_UGX", 0.0);
                if (cfg > 0) { contractSum = cfg; contractSrc = "COST_CONTRACT_SUM_UGX (config)"; }
                else if (prior != null && prior.ContractSumUGX > 0)
                { contractSum = prior.ContractSumUGX; contractSrc = "saved final_account.json"; }
                else
                {
                    var snap = PickAwardSnapshot(doc);
                    if (snap != null) { contractSum = snap.GrandTotalUGX; contractSrc = $"snapshot \"{snap.Label}\" ({snap.Type})"; }
                    else { contractSum = asBuilt; contractSrc = "live bill (NOT frozen — set COST_CONTRACT_SUM_UGX or save a tender snapshot)"; }
                }

                // 2. Provisional / PC-sum reconciliation trail.
                var psStore = BoqProvisionalTrail.Load(doc);
                var psRecords = psStore?.Records ?? new List<BoqProvisionalRecord>();
                var reconciled = psRecords.Where(r => r.ReconciledActual.HasValue).ToList();
                double psAllowances = reconciled.Sum(r => r.OriginalSum);
                double psActuals = reconciled.Sum(r => r.ReconciledActual.Value);
                double psMovement = psActuals - psAllowances;   // == BoqProvisionalTrail.MovementUGX

                // 3. Agreed variations (Approved + Incorporated).
                var vos = VariationEngine.ListVariations(doc)
                    .Select(VariationEngine.Load).Where(v => v != null).ToList();
                var agreed = vos.Where(v => v.Status == VariationStatus.Approved
                                         || v.Status == VariationStatus.Incorporated).ToList();
                double agreedVo = agreed.Sum(v => v.TotalValue);

                // 4. Fluctuations (index-linked / price-rise recovery total; 0 if none).
                double fluctuations = TagConfig.GetConfigDouble("COST_FLUCTUATIONS_UGX", 0.0);

                // 5. The waterfall.
                double finalAccount = Math.Round(contractSum + psMovement + agreedVo + fluctuations, 0);

                // 6. Sign-off (draft until a BoqSignOff is recorded).
                var signoff = BoqSignOffStore.Load(doc);

                var stmt = new FinalAccountStatement
                {
                    ProjectName = boq.ProjectName ?? doc.Title,
                    GeneratedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    Currency = ccy,
                    ContractSumUGX = Math.Round(contractSum, 0),
                    ContractSumSource = contractSrc,
                    ProvisionalAllowancesUGX = Math.Round(psAllowances, 0),
                    ProvisionalActualsUGX = Math.Round(psActuals, 0),
                    ProvisionalMovementUGX = Math.Round(psMovement, 0),
                    AgreedVariationsUGX = Math.Round(agreedVo, 0),
                    AgreedVariationCount = agreed.Count,
                    FluctuationsUGX = Math.Round(fluctuations, 0),
                    FinalAccountUGX = finalAccount,
                    AsBuiltRemeasureUGX = Math.Round(asBuilt, 0),
                    AsBuiltVarianceUGX = Math.Round(asBuilt - contractSum, 0),
                    SignedBy = signoff?.SignedBy ?? "",
                    SignedRole = signoff?.Role ?? "",
                    SignedDate = signoff?.Date ?? "",
                    Variations = agreed.Select(v => new FinalAccountVariationRow
                    {
                        Number = v.Number, Kind = v.Kind.ToString(), Status = v.Status.ToString(),
                        ValueUGX = Math.Round(v.TotalValue, 0), ApprovedBy = v.ApprovedBy,
                        ApprovalDate = v.ApprovalDate?.ToString("yyyy-MM-dd") ?? ""
                    }).ToList(),
                    Provisionals = psRecords.Select(r => new FinalAccountProvisionalRow
                    {
                        Description = r.Description,
                        OriginalSumUGX = Math.Round(r.OriginalSum, 0),
                        ReconciledActualUGX = r.ReconciledActual.HasValue ? Math.Round(r.ReconciledActual.Value, 0) : (double?)null,
                        MovementUGX = r.ReconciledActual.HasValue ? Math.Round(r.ReconciledActual.Value - r.OriginalSum, 0) : 0,
                        Status = r.Status
                    }).ToList()
                };

                FinalAccountStore.Save(doc, stmt);
                string xlsx = ExportXlsx(doc, stmt, signoff, boq);

                bool signed = signoff != null && !string.IsNullOrWhiteSpace(signoff.SignedBy);
                var panel = StingResultPanel.Create("Final Account Reconciliation")
                    .AddSection(signed ? "CERTIFIED" : "DRAFT (record a QS sign-off to certify)")
                    .Metric("Contract Sum", $"{ccy} {stmt.ContractSumUGX:N0}")
                    .Text($"   source: {contractSrc}")
                    .Metric("± Provisional/PC movement", $"{Sign(psMovement)}{ccy} {Math.Abs(psMovement):N0}")
                    .Metric($"± Agreed variations ({agreed.Count})", $"{Sign(agreedVo)}{ccy} {Math.Abs(agreedVo):N0}")
                    .Metric("± Fluctuations", $"{Sign(fluctuations)}{ccy} {Math.Abs(fluctuations):N0}")
                    .Metric("= FINAL ACCOUNT", $"{ccy} {finalAccount:N0}")
                    .AddSection("AS-BUILT BASIS")
                    .Metric("As-built remeasure (live)", $"{ccy} {stmt.AsBuiltRemeasureUGX:N0}")
                    .Metric("Variance vs Contract Sum", $"{Sign(stmt.AsBuiltVarianceUGX)}{ccy} {Math.Abs(stmt.AsBuiltVarianceUGX):N0}");
                if (xlsx != null) panel.SetCsvPath(xlsx);
                panel.Show();

                StingLog.Info($"Final account reconciled: {ccy} {finalAccount:N0} (contract {stmt.ContractSumUGX:N0}, " +
                              $"ps {Sign(psMovement)}{Math.Abs(psMovement):N0}, vo {Sign(agreedVo)}{Math.Abs(agreedVo):N0}).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FinalAccount_Reconcile", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Sign(double v) => v >= 0 ? "+" : "-";

        private static BOQSnapshotMeta PickAwardSnapshot(Document doc)
        {
            try
            {
                var snaps = BOQCostManager.ListSnapshots(doc);
                if (snaps == null || snaps.Count == 0) return null;
                // Prefer an award/tender/contract snapshot, else the most recent.
                string[] pref = { "award", "contract", "tender", "dd" };
                foreach (var key in pref)
                {
                    var hit = snaps.Where(s => (s.Type ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                                            || (s.Label ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                   .OrderByDescending(s => s.Date).FirstOrDefault();
                    if (hit != null && hit.GrandTotalUGX > 0) return hit;
                }
                return snaps.Where(s => s.GrandTotalUGX > 0).OrderByDescending(s => s.Date).FirstOrDefault();
            }
            catch (Exception ex) { StingLog.Warn($"PickAwardSnapshot: {ex.Message}"); return null; }
        }

        private static string ExportXlsx(Document doc, FinalAccountStatement s, BoqSignOff signoff, BOQDocument boq)
        {
            try
            {
                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_FinalAccount", ".xlsx");
                using (var wb = new XLWorkbook())
                {
                    // Sheet 1 — the reconciliation waterfall.
                    var ws = wb.AddWorksheet("Final Account");
                    ws.Column(1).Width = 3;
                    ws.Column(2).Width = 56;
                    ws.Column(3).Width = 22;

                    int r = 2;
                    ws.Cell(r, 2).Value = "FINAL ACCOUNT STATEMENT";
                    ws.Cell(r, 2).Style.Font.SetBold(true).Font.SetFontSize(15);
                    r++;
                    ws.Cell(r, 2).Value = s.ProjectName; r++;
                    ws.Cell(r, 2).Value = $"Generated {s.GeneratedDate}   ·   Currency {s.Currency}"; r += 2;

                    void Line(string label, double val, bool heavy = false)
                    {
                        ws.Cell(r, 2).Value = label;
                        ws.Cell(r, 3).Value = val;
                        ws.Cell(r, 3).Style.NumberFormat.SetFormat("#,##0");
                        if (heavy)
                        {
                            ws.Range(r, 2, r, 3).Style.Font.SetBold(true)
                              .Border.SetTopBorder(XLBorderStyleValues.Thin)
                              .Border.SetBottomBorder(XLBorderStyleValues.Double);
                        }
                        r++;
                    }

                    Line("Contract Sum (frozen at award, incl. VAT)", s.ContractSumUGX, true);
                    ws.Cell(r, 2).Value = $"   basis: {s.ContractSumSource}";
                    ws.Cell(r, 2).Style.Font.SetItalic(true).Font.SetFontSize(9); r += 2;

                    Line("Less: provisional / PC-sum allowances in contract", -s.ProvisionalAllowancesUGX);
                    Line("Add: reconciled provisional / PC-sum actuals", s.ProvisionalActualsUGX);
                    Line($"Add/Less: agreed variations ({s.AgreedVariationCount})", s.AgreedVariationsUGX);
                    Line("Add/Less: fluctuations", s.FluctuationsUGX);
                    r++;
                    Line("FINAL ACCOUNT", s.FinalAccountUGX, true);
                    r += 2;

                    ws.Cell(r, 2).Value = "AS-BUILT BASIS (remeasure)"; ws.Cell(r, 2).Style.Font.SetBold(true); r++;
                    Line("As-built remeasure (live canonical total)", s.AsBuiltRemeasureUGX);
                    Line("Variance vs Contract Sum", s.AsBuiltVarianceUGX);
                    r += 2;

                    string status = !string.IsNullOrWhiteSpace(s.SignedBy)
                        ? $"CERTIFIED by {s.SignedBy} ({s.SignedRole}) on {s.SignedDate}"
                        : "DRAFT — record a QS sign-off (Actions → Record QS Sign-off) to certify.";
                    ws.Cell(r, 2).Value = status;
                    ws.Cell(r, 2).Style.Font.SetBold(true).Font.SetFontColor(
                        !string.IsNullOrWhiteSpace(s.SignedBy) ? XLColor.DarkGreen : XLColor.DarkRed);

                    // Sheet 2 — variations annexure.
                    var wv = wb.AddWorksheet("Variations");
                    wv.Cell(1, 1).Value = "Number"; wv.Cell(1, 2).Value = "Kind";
                    wv.Cell(1, 3).Value = "Status"; wv.Cell(1, 4).Value = "Value";
                    wv.Cell(1, 5).Value = "Approved by"; wv.Cell(1, 6).Value = "Approval date";
                    wv.Row(1).Style.Font.SetBold(true);
                    int rv = 2;
                    foreach (var v in s.Variations)
                    {
                        wv.Cell(rv, 1).Value = v.Number; wv.Cell(rv, 2).Value = v.Kind;
                        wv.Cell(rv, 3).Value = v.Status; wv.Cell(rv, 4).Value = v.ValueUGX;
                        wv.Cell(rv, 4).Style.NumberFormat.SetFormat("#,##0");
                        wv.Cell(rv, 5).Value = v.ApprovedBy; wv.Cell(rv, 6).Value = v.ApprovalDate;
                        rv++;
                    }
                    wv.Columns().AdjustToContents();

                    // Sheet 3 — provisional / PC-sum reconciliation annexure.
                    var wp = wb.AddWorksheet("Provisional Sums");
                    wp.Cell(1, 1).Value = "Description"; wp.Cell(1, 2).Value = "Original sum";
                    wp.Cell(1, 3).Value = "Reconciled actual"; wp.Cell(1, 4).Value = "Movement";
                    wp.Cell(1, 5).Value = "Status";
                    wp.Row(1).Style.Font.SetBold(true);
                    int rp = 2;
                    foreach (var p in s.Provisionals)
                    {
                        wp.Cell(rp, 1).Value = p.Description;
                        wp.Cell(rp, 2).Value = p.OriginalSumUGX;
                        if (p.ReconciledActualUGX.HasValue) wp.Cell(rp, 3).Value = p.ReconciledActualUGX.Value;
                        wp.Cell(rp, 4).Value = p.MovementUGX;
                        wp.Cell(rp, 5).Value = p.Status;
                        wp.Range(rp, 2, rp, 4).Style.NumberFormat.SetFormat("#,##0");
                        rp++;
                    }
                    wp.Columns().AdjustToContents();

                    // Reuse the QS sign-off stamp (draft watermark until signed).
                    try { BoqSignOffStore.StampWorkbook(wb, doc, boq); }
                    catch (Exception ex) { StingLog.Warn($"FinalAccount sign-off stamp: {ex.Message}"); }

                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"FinalAccount ExportXlsx: {ex.Message}"); return null; }
        }
    }
}
